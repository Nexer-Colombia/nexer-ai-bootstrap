namespace NexerAI.Core.Ports;

/// <summary>Interactive questions, so tests and non-interactive runs can supply answers.</summary>
/// <remarks>
/// Every method throws <see cref="PromptUnavailableException"/> when it cannot ask (no interactive console), and
/// <see cref="OperationCanceledException"/> when input ends before an answer. How the person cancels depends on
/// the method, see each one: an implementation may let Ctrl+C end the process instead of throwing.
/// </remarks>
public interface IPrompter
{
    /// <summary>Asks the person to pick one of <paramref name="choices"/> and returns it.</summary>
    /// <remarks>
    /// The console implementation reads a line, so Ctrl+C ends the process like in any console program; end of
    /// input (Ctrl+Z then Enter) throws <see cref="OperationCanceledException"/>.
    /// </remarks>
    string Choose(string question, IReadOnlyList<string> choices);

    /// <summary>Reads a secret with hidden input: nothing is echoed, not even a mask character.</summary>
    /// <remarks>
    /// Returns a plain string: <c>SecureString</c> is not recommended on modern .NET. The console implementation
    /// intercepts Ctrl+C like Escape, so the secret buffer is cleared and the caller can clean up: both throw
    /// <see cref="OperationCanceledException"/>.
    /// </remarks>
    string ReadSecret(string prompt);

    /// <summary>Asks a yes/no question.</summary>
    /// <remarks>
    /// The console implementation reads a line, so Ctrl+C ends the process like in any console program; end of
    /// input (Ctrl+Z then Enter) throws <see cref="OperationCanceledException"/>.
    /// </remarks>
    bool Confirm(string question);
}
