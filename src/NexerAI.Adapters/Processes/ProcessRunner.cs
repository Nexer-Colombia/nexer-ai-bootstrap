using System.Buffers;
using System.Diagnostics;
using System.Text;

namespace NexerAI.Adapters.Processes;

/// <summary>
/// Runs programs with <see cref="Process"/>: no shell, no window, standard input closed, both outputs captured.
/// A batch file (<c>.cmd</c>, <c>.bat</c>) is run by <c>cmd.exe</c>, which Windows starts implicitly and which
/// re-reads the command line with its own rules (<c>&amp;</c>, <c>|</c>, <c>%</c>...); <see cref="Process"/> does not
/// escape for it, so a batch file is refused unless its path and arguments contain only characters
/// <c>cmd.exe</c> leaves alone. A space is allowed in an argument: <see cref="Process"/> then wraps the argument in
/// quotes, inside which <c>cmd.exe</c> reads everything literally except <c>%</c>, <c>!</c> and <c>"</c>, all refused.
/// A UNC working directory is refused too: <c>cmd.exe</c> cannot start in one and runs in the Windows folder instead.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Windows error <c>ERROR_FILE_NOT_FOUND</c>: the program does not exist.</summary>
    public const int ErrorFileNotFound = 2;

    /// <summary>Windows error <c>ERROR_PATH_NOT_FOUND</c>: a folder of the program's path does not exist.</summary>
    public const int ErrorPathNotFound = 3;

    /// <summary>Windows error <c>ERROR_DIRECTORY</c>: the working directory does not exist or is not a folder.</summary>
    public const int ErrorDirectory = 267;

    /// <summary>
    /// What a batch file argument may hold from ASCII: letters, digits, <c>@._:/\=+,-</c> and the space. A space makes
    /// <see cref="Process"/> quote the argument, so a per-user path such as <c>C:/Users/Ana Maria/...</c> stays one
    /// argument; none of the other characters needs quoting. Beyond ASCII only letters and digits are allowed
    /// (<c>C:/Users/María/...</c>): punctuation and symbols are refused because a console codepage can map
    /// them to syntax characters (fullwidth <c>＂</c> to a quote, <c>％</c> to <c>%</c>).
    /// </summary>
    private static readonly SearchValues<char> BatchArgumentCharacters =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789@._:/\\=+,- ");

    /// <summary>
    /// What <c>cmd.exe</c> expands even inside quotes (<c>%</c>, <c>!</c>, <c>"</c>), or treats as syntax when
    /// <see cref="Process"/> leaves a path without spaces unquoted (<c>&amp;|&lt;&gt;^</c>): a batch file path may not hold it.
    /// </summary>
    private static readonly SearchValues<char> BatchPathExpansions = SearchValues.Create("%!\"&|<>^");

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct, string? standardInput = null)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (IsBatchFile(fileName))
        {
            EnsureSafeForCmd(fileName, arguments, workingDirectory);
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            StandardInputEncoding = Utf8,
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
        if (standardInput is not null)
        {
            // A program that exits without reading its input closes the pipe: that is not an error here.
            try
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }

        try
        {
            process.StandardInput.Close(); // Nothing more to answer: a prompt reads end of input instead of waiting.
        }
        catch (IOException)
        {
        }

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
    /// character <c>cmd.exe</c> interprets (<see cref="BatchPathExpansions"/>), a UNC working directory, or an argument
    /// outside <see cref="BatchArgumentCharacters"/>. The message names the file but never an argument.
    /// </summary>
    private static void EnsureSafeForCmd(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (!Path.IsPathFullyQualified(fileName) || fileName.AsSpan().ContainsAny(BatchPathExpansions))
        {
            throw new ArgumentException($"The batch file '{fileName}' cannot be run safely: it must be a full path without % ! \" & | < > or ^.", nameof(fileName));
        }

        if (workingDirectory.StartsWith(@"\\", StringComparison.Ordinal) || workingDirectory.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The batch file '{fileName}' cannot run in the UNC working directory '{workingDirectory}': cmd.exe would run it in the Windows folder instead. Use a drive path.", nameof(workingDirectory));
        }

        foreach (var argument in arguments)
        {
            if (argument.Length == 0 || !IsSafeBatchArgument(argument))
            {
                throw new ArgumentException($"An argument for the batch file '{fileName}' holds characters cmd.exe would interpret.", nameof(arguments));
            }
        }
    }

    private static bool IsSafeBatchArgument(string argument)
    {
        foreach (var character in argument)
        {
            if (!BatchArgumentCharacters.Contains(character) && !(character > 127 && char.IsLetterOrDigit(character)))
            {
                return false;
            }
        }

        return true;
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
