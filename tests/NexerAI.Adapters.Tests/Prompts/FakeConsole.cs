using System.Text;
using NexerAI.Adapters.Prompts;

namespace NexerAI.Adapters.Tests.Prompts;

/// <summary>
/// Replays scripted lines and records what was written. Running out of script fails the test with
/// <see cref="InvalidDataException"/>, which the prompter does not catch.
/// </summary>
internal sealed class FakeConsole : IConsole
{
    private readonly StringBuilder output = new();

    public bool IsInputRedirected { get; init; }

    /// <summary>Lines for <see cref="ReadLine"/>; <see langword="null"/> is end of input.</summary>
    public Queue<string?> Lines { get; } = new();

    public string Output => output.ToString();

    public string? ReadLine() =>
        Lines.TryDequeue(out var line) ? line : throw new InvalidDataException("No scripted line left.");

    public void Write(string text) => output.Append(text);
}
