using System.Text;
using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.ClaudeCode;

/// <summary>
/// Changes Claude Code's user <c>settings.json</c> as a read-modify-write: the new text goes to a temporary file in the
/// same folder, flushed to disk and moved over the file, so a crash never leaves it half written. Claude Code holds no
/// lock and rewrites the whole file when it saves a setting, so the file is read again just before the move, and a
/// change made meanwhile is not overwritten: the update starts again from the new text. A write by Claude Code between
/// that check and the move can still be lost; the window is a few milliseconds. The text must be valid UTF-8: a file
/// that is not (for example one saved in an ANSI code page) is refused and left alone, because decoding it leniently
/// would write replacement characters over the person's values. When the file is a symbolic link (dotfiles setups) the
/// update goes to the file the link points at, so the link stays; a hard link cannot be detected portably and is
/// replaced by a regular file, as is any file replaced by a move.
/// </summary>
public sealed class ClaudeSettingsFile
{
    private const int MaxAttempts = 3;

    private readonly Func<string, string?> resolveLink;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Creates the file wrapper.</summary>
    /// <param name="filePath">Absolute path of the file, usually from <see cref="ClaudeSettings.FilePath"/>.</param>
    public ClaudeSettingsFile(string filePath)
        : this(filePath, FinalLinkTarget)
    {
    }

    /// <summary>Creates the file wrapper with another way to find where a symbolic link points; tests use it.</summary>
    /// <param name="filePath">Absolute path of the file.</param>
    /// <param name="resolveLink">Returns the path a symbolic link finally points at, or <see langword="null"/> when the path is not a link.</param>
    internal ClaudeSettingsFile(string filePath, Func<string, string?> resolveLink)
    {
        ArgumentNullException.ThrowIfNull(resolveLink);
        this.resolveLink = resolveLink;

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
    /// Reads the text (strict UTF-8, a byte order mark dropped; <see langword="null"/> when the file does not exist), passes it
    /// to <paramref name="change"/> and writes what it returns as UTF-8 without a byte order mark, creating the folder
    /// if needed. Nothing is written when <paramref name="change"/> returns <see langword="null"/> or throws.
    /// </summary>
    /// <param name="change">Returns the new text, or <see langword="null"/> when nothing needs writing. It may run more than once.</param>
    /// <returns>Whether the file was written.</returns>
    /// <exception cref="AgentStateException">
    /// The file cannot be read or written, is not valid UTF-8, or was busy or changed on every one of three attempts; the
    /// message names the path, never the content.
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
                    $"Claude Code's settings file '{FilePath}' was busy or kept changing while nexer-ai updated it; it was left as it was. Try again.");
            }

            await Task.Delay(RetryDelay, ct).ConfigureAwait(false);
        }
    }

    private static string? FinalLinkTarget(string path)
    {
        try
        {
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // Not a link, or not readable: the read reports a real problem.
        }
    }

    /// <summary>The file to read and replace: <see cref="FilePath"/>, or what it links to.</summary>
    private string TargetPath() => resolveLink(FilePath) is { Length: > 0 } target ? target : FilePath;

    private async Task<string?> ReadAsync(CancellationToken ct)
    {
        try
        {
            using var stream = new FileStream(TargetPath(), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, StrictUtf8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (DecoderFallbackException ex)
        {
            // The bytes are not quoted: they are the person's settings.
            throw new AgentStateException(
                $"Claude Code's settings file '{FilePath}' is not valid UTF-8 (a file saved in another encoding?); nexer-ai left it untouched. Save it as UTF-8 and run nexer-ai again.", ex);
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
        var targetPath = TargetPath();
        var tempPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
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
                File.Move(tempPath, targetPath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (IsSharingViolation(ex, targetPath))
            {
                // A reader such as Claude Code or an antivirus scan has the file open. Windows reports that as an
                // IOException or, for a handle opened without delete sharing, as an access error.
                return false;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AgentStateException(
                $"Claude Code's settings file '{FilePath}' cannot be written: access was denied (read-only file or folder permissions); nexer-ai left it untouched.", ex);
        }
        catch (IOException ex)
        {
            throw new AgentStateException($"Claude Code's settings file '{FilePath}' cannot be written: {ex.Message}", ex);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Whether a refused move is a busy file to retry. An access error is classified by probing the file for writing: a
    /// sharing violation there means another program has it open (retry), while a denial means a permanent refusal such
    /// as a permission rule or a read-only file, reported at once.
    /// </summary>
    private static bool IsSharingViolation(Exception ex, string path) =>
        ex switch
        {
            FileNotFoundException or DirectoryNotFoundException => false,
            IOException => File.Exists(path),
            UnauthorizedAccessException => File.Exists(path) && !File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly) && !WriteIsDenied(path),
            _ => false,
        };

    private static bool WriteIsDenied(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false; // A sharing violation: the file is in use.
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
