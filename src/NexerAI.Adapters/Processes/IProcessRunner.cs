namespace NexerAI.Adapters.Processes;

/// <summary>
/// Runs a program to completion, for adapters that drive a command line tool such as <c>claude</c>. A seam for
/// adapter tests, not a port of the core: the core never starts processes.
/// </summary>
public interface IProcessRunner
{
    /// <summary>Starts <paramref name="fileName"/> in <paramref name="workingDirectory"/>, waits for it to exit and returns what it wrote.</summary>
    /// <param name="fileName">The program, found on <c>PATH</c> like <c>CreateProcess</c> does (an <c>.exe</c>, not a <c>.cmd</c>).</param>
    /// <param name="arguments">Each argument as one item, quoted by the runner.</param>
    /// <param name="workingDirectory">An absolute folder.</param>
    /// <param name="ct">Cancels the wait and kills the process tree.</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The program cannot be started, for example because it is not on <c>PATH</c>.</exception>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct);
}

/// <summary>How a program ended: its exit code and its standard output and error, read as UTF-8.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
