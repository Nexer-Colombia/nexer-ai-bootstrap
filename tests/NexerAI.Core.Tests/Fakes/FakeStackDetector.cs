using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeStackDetector : IStackDetector
{
    public IReadOnlyList<StackSignal> Signals { get; init; } = [];

    public List<string> DetectedPaths { get; } = [];

    public IReadOnlyList<StackSignal> Detect(string projectPath)
    {
        DetectedPaths.Add(projectPath);
        return Signals;
    }
}
