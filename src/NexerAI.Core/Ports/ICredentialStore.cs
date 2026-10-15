using System.Diagnostics.CodeAnalysis;

namespace NexerAI.Core.Ports;

/// <summary>Per-user secret store for tracker tokens, keyed by tracker URL.</summary>
[SuppressMessage("Naming", "CA1716", Justification = "C#-only code base; Get/Set read naturally.")]
public interface ICredentialStore
{
    /// <summary>Whether a secret is stored for the tracker.</summary>
    bool Exists(Uri trackerUrl);

    /// <summary>Returns the stored secret, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="Credentials.CredentialStoreException">A secret is stored but cannot be read back.</exception>
    string? Get(Uri trackerUrl);

    /// <summary>Stores or replaces the secret for the tracker.</summary>
    void Set(Uri trackerUrl, string secret);
}
