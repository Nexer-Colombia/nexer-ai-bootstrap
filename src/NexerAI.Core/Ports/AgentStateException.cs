namespace NexerAI.Core.Ports;

/// <summary>
/// The agent's actual state cannot be read: its command failed, or its output or configuration file has a shape
/// this version does not recognize. The message names the source and never quotes its content, which can hold
/// secrets; <c>doctor</c> shows it as a warning and the other commands stop with it.
/// </summary>
public sealed class AgentStateException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public AgentStateException()
        : base("The agent's state cannot be read.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public AgentStateException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public AgentStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
