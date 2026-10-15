using NexerAI.Adapters.Processes;

namespace NexerAI.Adapters.Tests.Processes;

/// <summary>Records each run and answers with <see cref="Result"/>, or throws <see cref="Exception"/> when set.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessResult Result { get; init; } = new(0, string.Empty, string.Empty);

    public Exception? Exception { get; init; }

    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Runs { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken ct)
    {
        Runs.Add((fileName, arguments, workingDirectory));
        return Exception is null ? Task.FromResult(Result) : Task.FromException<ProcessResult>(Exception);
    }
}
