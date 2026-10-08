using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

/// <summary>Answers from queues; an unexpected question fails the test.</summary>
public sealed class FakePrompter : IPrompter
{
    public Queue<string> Answers { get; } = new();

    public Queue<bool> Confirmations { get; } = new();

    public List<string> Questions { get; } = [];

    public string Choose(string question, IReadOnlyList<string> choices)
    {
        var answer = Next(Answers, question);
        return choices.Contains(answer)
            ? answer
            : throw new InvalidOperationException($"Canned answer '{answer}' is not a choice of '{question}'.");
    }

    public string ReadSecret(string prompt) => Next(Answers, prompt);

    public bool Confirm(string question) => Next(Confirmations, question);

    private T Next<T>(Queue<T> answers, string question)
    {
        Questions.Add(question);
        return answers.TryDequeue(out var answer)
            ? answer
            : throw new InvalidOperationException($"No canned answer for '{question}'.");
    }
}
