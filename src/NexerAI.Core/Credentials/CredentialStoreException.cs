namespace NexerAI.Core.Credentials;

/// <summary>
/// A stored credential exists but cannot be read back: the file cannot be read, is corrupt, was copied from
/// another entry or was encrypted for another user or machine. The message never contains the secret.
/// </summary>
public sealed class CredentialStoreException : Exception
{
    /// <summary>Creates the exception with a default message.</summary>
    public CredentialStoreException()
        : base("A stored credential cannot be read.")
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/>.</summary>
    public CredentialStoreException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with <paramref name="message"/> and the exception that caused it.</summary>
    public CredentialStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
