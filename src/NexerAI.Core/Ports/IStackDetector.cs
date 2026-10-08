using NexerAI.Core.Domain;

namespace NexerAI.Core.Ports;

/// <summary>Detects technologies in a working copy to suggest stack plugins.</summary>
public interface IStackDetector
{
    /// <summary>Returns the signals found under <paramref name="projectPath"/>.</summary>
    IReadOnlyList<StackSignal> Detect(string projectPath);
}
