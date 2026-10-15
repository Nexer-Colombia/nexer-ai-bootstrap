using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace NexerAI.Adapters.Processes;

/// <summary>
/// Runs programs with <see cref="Process"/>: no shell, no window, standard input closed, both outputs captured.
/// A batch file (<c>.cmd</c>, <c>.bat</c>) is run by <c>cmd.exe</c>, which Windows starts implicitly and which
/// re-reads the command line with its own rules (<c>&amp;</c>, <c>|</c>, <c>%</c>...); <see cref="Process"/> does not
/// escape for it, so a batch file is refused unless its path and arguments contain only characters
/// <c>cmd.exe</c> leaves alone.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Windows error <c>ERROR_FILE_NOT_FOUND</c>: the program does not exist.</summary>
    public const int ErrorFileNotFound = 2;

    /// <summary>Windows error <c>ERROR_PATH_NOT_FOUND</c>: a folder of the program's path does not exist.</summary>
    public const int ErrorPathNotFound = 3;

    /// <summary>Windows error <c>ERROR_DIRECTORY</c>: the working directory does not exist or is not a folder.</summary>
    public const int ErrorDirectory = 267;

    /// <summary>What a batch file argument may hold: letters, digits and <c>@._:/\=+,-</c>.</summary>
    private static readonly SearchValues<char> BatchArgumentCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@._:/\\=+,-");

    /// <summary>What <c>cmd.exe</c> expands even inside quotes, so a batch file path may not hold it.</summary>
    private static readonly SearchValues<char> BatchPathExpansions = SearchValues.Create("%!\"");

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (IsBatchFile(fileName))
        {
            EnsureSafeForCmd(fileName, arguments);
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close(); // Nothing to answer: a prompt reads end of input instead of waiting.
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static bool IsBatchFile(string fileName) =>
        Path.GetExtension(fileName).ToUpperInvariant() is ".CMD" or ".BAT";

    /// <summary>
    /// Refuses a batch file that is not a full path (no search of the current folder), whose path holds a
    /// character <c>cmd.exe</c> expands even inside quotes (<c>%</c>, <c>!</c>, <c>"</c>), or an argument outside
    /// <see cref="BatchArgumentCharacters"/>. The message names the file but never an argument.
    /// </summary>
    private static void EnsureSafeForCmd(string fileName, IReadOnlyList<string> arguments)
    {
        if (!Path.IsPathFullyQualified(fileName) || fileName.AsSpan().ContainsAny(BatchPathExpansions))
        {
            throw new ArgumentException($"The batch file '{fileName}' cannot be run safely: it must be a full path without %, ! or quotes.", nameof(fileName));
        }

        foreach (var argument in arguments)
        {
            if (argument.Length == 0 || argument.AsSpan().ContainsAnyExcept(BatchArgumentCharacters))
            {
                throw new ArgumentException($"An argument for the batch file '{fileName}' holds characters cmd.exe would interpret.", nameof(arguments));
            }
        }
    }

    /// <summary>
    /// Kills the process tree on cancellation without hiding the cancellation: <see cref="Process.Kill(bool)"/>
    /// throws when part of the tree cannot be terminated or the process is no longer associated.
    /// </summary>
    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception)
        {
            // Best effort: the caller asked to stop waiting, and that is what it is told.
        }
    }
}
