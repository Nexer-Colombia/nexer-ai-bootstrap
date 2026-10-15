using NexerAI.Adapters.Processes;

namespace NexerAI.Adapters.Tests.Processes;

/// <summary>
/// Records each run and answers with the next of <see cref="Results"/>, then with <see cref="Result"/>, or throws
/// <see cref="Exception"/> when set.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessResult Result { get; init; } = new(0, string.Empty, string.Empty);

    public Queue<ProcessResult> Results { get; init; } = [];

    public Exception? Exception { get; init; }

    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Runs { get; } = [];

    /// <summary>What each run was given on standard input, in the order of <see cref="Runs"/>; <see langword="null"/> for none.</summary>
    public List<string?> Inputs { get; } = [];

    public Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct, string? standardInput = null)
    {
        Runs.Add((fileName, arguments, workingDirectory));
        Inputs.Add(standardInput);
        return Exception is null
            ? Task.FromResult(Results.TryDequeue(out var next) ? next : Result)
            : Task.FromException<ProcessResult>(Exception);
    }
}
