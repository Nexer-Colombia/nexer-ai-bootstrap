namespace NexerAI.Core.State;

/// <summary>The state file exists but cannot be understood: malformed JSON, an unsupported schema or an invalid value.</summary>
public sealed class StateFileException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public StateFileException()
        : base("The state file is invalid.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public StateFileException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public StateFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
