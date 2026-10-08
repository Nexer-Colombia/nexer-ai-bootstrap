namespace NexerAI.Core.Ports;

/// <summary>Interactive questions, so tests and non-interactive runs can supply answers.</summary>
public interface IPrompter
{
    /// <summary>Asks the person to pick one of <paramref name="choices"/> and returns it.</summary>
    string Choose(string question, IReadOnlyList<string> choices);

    /// <summary>Reads a secret with masked input.</summary>
    /// <remarks>Returns a plain string: <c>SecureString</c> is not recommended on modern .NET.</remarks>
    string ReadSecret(string prompt);

    /// <summary>Asks a yes/no question.</summary>
    bool Confirm(string question);
}
