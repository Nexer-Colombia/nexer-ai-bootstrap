namespace NexerAI.Adapters.Processes;

/// <summary>
/// Runs a program to completion, for adapters that drive a command line tool such as <c>claude</c>. A seam for
/// adapter tests, not a port of the core: the core never starts processes.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Starts <paramref name="fileName"/> in <paramref name="workingDirectory"/>, waits for it to exit and returns what it wrote.</summary>
    /// <param name="fileName">
    /// The program: a full path, or a bare name found like <c>CreateProcess</c> does (an <c>.exe</c> only). A batch
    /// file (<c>.cmd</c>, <c>.bat</c>) must be a full path.
    /// </param>
    /// <param name="arguments">Each argument as one item, quoted by the runner.</param>
    /// <param name="workingDirectory">An absolute folder.</param>
    /// <param name="ct">Cancels the wait and kills the process tree.</param>
    /// <param name="standardInput">
    /// Text written to the program's standard input as UTF-8 without a byte order mark, then closed; <see langword="null"/>
    /// for none. How to pass values that must stay off the command line.
    /// </param>
    /// <exception cref="System.ComponentModel.Win32Exception">
    /// The program cannot be started; <see cref="System.ComponentModel.Win32Exception.NativeErrorCode"/> tells why,
    /// for example <see cref="ProcessRunner.ErrorFileNotFound"/> or <see cref="ProcessRunner.ErrorDirectory"/>.
    /// </exception>
    /// <exception cref="ArgumentException">A batch file whose path or arguments <c>cmd.exe</c> would interpret.</exception>
    Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct, string? standardInput = null);
}

/// <summary>How a program ended: its exit code and its standard output and error, read as UTF-8.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
