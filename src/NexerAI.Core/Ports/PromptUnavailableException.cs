namespace NexerAI.Core.Ports;

/// <summary>
/// A question cannot be asked because there is no interactive console: standard input is redirected or the
/// terminal cannot read keys. The message names the question, never an answer, and points to the options of
/// <c>--non-interactive</c>.
/// </summary>
public sealed class PromptUnavailableException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public PromptUnavailableException()
        : base("A question needs an interactive console.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public PromptUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public PromptUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
