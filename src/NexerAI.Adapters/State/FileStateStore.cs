using System.Diagnostics;
using System.Globalization;
using System.Text;
using NexerAI.Core.Ports;
using NexerAI.Core.State;

namespace NexerAI.Adapters.State;

/// <summary>
/// Stores the state file on disk (design 4.4). Updates are serialized across processes by an exclusive
/// lock on the sibling file <c>state.json.lock</c>, which is kept between runs, and written to a temporary
/// file in the same directory that is flushed to disk and then moved over the state file, so a crash never
/// leaves a half-written file and readers see either the old or the new content.
/// </summary>
public sealed class FileStateStore : IStateStore
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);

    private const int ErrorLockViolation = unchecked((int)0x80070021);

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMilliseconds(10);

    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(250);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeSpan lockTimeout;

    /// <summary>Creates a store for the state file at <paramref name="filePath"/> that waits up to <see cref="DefaultLockTimeout"/> for the lock.</summary>
    /// <param name="filePath">Fully qualified path of the state file, usually <see cref="DefaultPath"/>.</param>
    public FileStateStore(string filePath)
        : this(filePath, DefaultLockTimeout)
    {
    }

    /// <summary>Creates a store for the state file at <paramref name="filePath"/> that waits up to <paramref name="lockTimeout"/> for the lock.</summary>
    /// <param name="filePath">Fully qualified path of the state file, usually <see cref="DefaultPath"/>.</param>
    /// <param name="lockTimeout">
    /// How long <see cref="UpdateAsync"/> waits for another update to release the lock, and then for other
    /// programs to close the state file; zero tries once.
    /// </param>
    public FileStateStore(string filePath, TimeSpan lockTimeout)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException($"The state file path '{filePath}' is not absolute.", nameof(filePath));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(lockTimeout, TimeSpan.Zero);
        FilePath = filePath;
        this.lockTimeout = lockTimeout;
    }

    /// <summary>The default wait for the lock: 10 seconds, far longer than one update takes.</summary>
    public static TimeSpan DefaultLockTimeout { get; } = TimeSpan.FromSeconds(10);

    /// <summary>The state file of the current user: <c>%USERPROFILE%\.nexer-ai\state.json</c>.</summary>
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexer-ai", "state.json");

    /// <summary>Fully qualified path of the state file.</summary>
    public string FilePath { get; }

    private string LockPath => FilePath + ".lock";

    /// <inheritdoc />
    /// <remarks>Does not take the lock: updates replace the file atomically, so a read sees a complete state.</remarks>
    /// <exception cref="StateFileException">The file cannot be understood; the message names its path.</exception>
    public Task<InstallState> ReadAsync(CancellationToken ct) => ReadCoreAsync(ct);

    /// <inheritdoc />
    /// <exception cref="StateFileException">The current file cannot be understood; the message names its path and nothing is written.</exception>
    /// <exception cref="TimeoutException">Another update held the lock for longer than the lock timeout.</exception>
    /// <exception cref="IOException">Another program kept the state file open for longer than the lock timeout, among other I/O errors.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="update"/> returned <see langword="null"/>.</exception>
    public async Task<InstallState> UpdateAsync(Func<InstallState, InstallState> update, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(update);

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var held = await AcquireLockAsync(ct).ConfigureAwait(false);

        var current = await ReadCoreAsync(ct).ConfigureAwait(false);
        var next = update(current) ?? throw new InvalidOperationException("The state update returned null.");
        await ReplaceAsync(StateFileFormat.Serialize(next), ct).ConfigureAwait(false);
        return next;
    }

    private async Task<InstallState> ReadCoreAsync(CancellationToken ct)
    {
        string json;
        try
        {
            // Share everything to get in nobody's way; Windows still refuses to move a file over an open one,
            // so ReplaceAsync retries while a read is in progress.
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
            json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return InstallState.Empty;
        }

        try
        {
            return StateFileFormat.Deserialize(json);
        }
        catch (StateFileException ex)
        {
            throw new StateFileException($"Cannot read the state file '{FilePath}': {ex.Message}", ex);
        }
    }

    /// <summary>Opens the lock file exclusively, waiting while another process holds it.</summary>
    private Task<FileStream> AcquireLockAsync(CancellationToken ct) =>
        RetryAsync(
            () => new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None),
            ex => ex is IOException { HResult: ErrorSharingViolation or ErrorLockViolation },
            (waited, ex) => new TimeoutException(
                $"Timed out after {waited} waiting for the lock on the state file '{FilePath}'. Another nexer-ai command may be updating it; try again when it finishes.",
                ex),
            ct);

    /// <summary>
    /// Writes <paramref name="json"/> to a temporary file, flushes it to disk and moves it over the state file.
    /// Windows refuses the move while another program (a reader, an antivirus scan) has the state file open,
    /// so it is retried like the lock.
    /// </summary>
    private async Task ReplaceAsync(string json, CancellationToken ct)
    {
        var tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(Utf8NoBom.GetBytes(json), ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            await RetryAsync(
                () =>
                {
                    File.Move(tempPath, FilePath, overwrite: true);
                    return true;
                },
                ex => ex is UnauthorizedAccessException or IOException { HResult: ErrorSharingViolation or ErrorLockViolation },
                (waited, ex) => new IOException(
                    $"Timed out after {waited} replacing the state file '{FilePath}'; another program keeps it open.", ex),
                ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> until it succeeds, retrying failures that <paramref name="isTransient"/>
    /// accepts with exponential backoff for up to the lock timeout; then throws what <paramref name="timedOut"/> returns.
    /// </summary>
    private async Task<T> RetryAsync<T>(
        Func<T> attempt, Func<Exception, bool> isTransient, Func<string, Exception, Exception> timedOut, CancellationToken ct)
    {
        var elapsed = Stopwatch.StartNew();
        var delay = FirstRetryDelay;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return attempt();
            }
            catch (Exception ex) when (isTransient(ex))
            {
                var remaining = lockTimeout - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw timedOut(string.Create(CultureInfo.InvariantCulture, $"{lockTimeout.TotalSeconds:0.###} s"), ex);
                }

                await Task.Delay(delay < remaining ? delay : remaining, ct).ConfigureAwait(false);
                delay = delay * 2 < MaxRetryDelay ? delay * 2 : MaxRetryDelay;
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the original error matters more than a leftover temporary file.
        }
    }
}
