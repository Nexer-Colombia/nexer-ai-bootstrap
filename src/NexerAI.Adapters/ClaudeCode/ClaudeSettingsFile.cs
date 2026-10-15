using System.Text;
using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.ClaudeCode;

/// <summary>
/// Changes Claude Code's user <c>settings.json</c> as a read-modify-write: the new text goes to a temporary file in the
/// same folder, flushed to disk and moved over the file, so a crash never leaves it half written. Claude Code holds no
/// lock and rewrites the whole file when it saves a setting, so the file is read again just before the move, and a
/// change made meanwhile is not overwritten: the update starts again from the new text. A write by Claude Code between
/// that check and the move can still be lost; the window is a few milliseconds.
/// </summary>
public sealed class ClaudeSettingsFile
{
    private const int MaxAttempts = 3;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Creates the file wrapper.</summary>
    /// <param name="filePath">Absolute path of the file, usually from <see cref="ClaudeSettings.FilePath"/>.</param>
    public ClaudeSettingsFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!Path.IsPathFullyQualified(filePath))
        {
            throw new ArgumentException($"The Claude Code settings file '{filePath}' is not absolute.", nameof(filePath));
        }

        FilePath = filePath;
    }

    /// <summary>Absolute path of the file.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Reads the text (UTF-8, a byte order mark dropped; <see langword="null"/> when the file does not exist), passes it
    /// to <paramref name="change"/> and writes what it returns as UTF-8 without a byte order mark, creating the folder
    /// if needed. Nothing is written when <paramref name="change"/> returns <see langword="null"/> or throws.
    /// </summary>
    /// <param name="change">Returns the new text, or <see langword="null"/> when nothing needs writing. It may run more than once.</param>
    /// <returns>Whether the file was written.</returns>
    /// <exception cref="AgentStateException">
    /// The file cannot be read or written, or it changed on every one of three attempts; the message names the path,
    /// never the content.
    /// </exception>
    public async Task<bool> UpdateAsync(Func<string?, string?> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);

        for (var attempt = 1; ; attempt++)
        {
            var original = await ReadAsync(ct).ConfigureAwait(false);
            if (change(original) is not { } updated)
            {
                return false;
            }

            if (await TryReplaceAsync(original, updated, ct).ConfigureAwait(false))
            {
                return true;
            }

            if (attempt == MaxAttempts)
            {
                throw new AgentStateException(
                    $"Claude Code's settings file '{FilePath}' kept changing while nexer-ai updated it; it was left as Claude Code wrote it. Try again.");
            }

            await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
        }
    }

    private async Task<string?> ReadAsync(CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Utf8NoBom, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AgentStateException($"Claude Code's settings file '{FilePath}' cannot be read: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Writes <paramref name="updated"/> to a temporary file and moves it over the file, unless the file no longer holds
    /// <paramref name="original"/>, or the move is refused because another program has the file open (both retried by
    /// the caller).
    /// </summary>
    private async Task<bool> TryReplaceAsync(string? original, string updated, CancellationToken ct)
    {
        var tempPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(Utf8NoBom.GetBytes(updated), ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (!string.Equals(await ReadAsync(ct).ConfigureAwait(false), original, StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                File.Move(tempPath, FilePath, overwrite: true);
                return true;
            }
            catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException && File.Exists(FilePath))
            {
                // Sharing violation: a reader such as Claude Code or an antivirus scan has the file open.
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AgentStateException($"Claude Code's settings file '{FilePath}' cannot be written: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(tempPath);
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
