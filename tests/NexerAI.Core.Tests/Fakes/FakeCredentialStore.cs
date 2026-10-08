using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeCredentialStore : ICredentialStore
{
    public Dictionary<Uri, string> Secrets { get; } = [];

    public bool Exists(Uri trackerUrl) => Secrets.ContainsKey(trackerUrl);

    public string? Get(Uri trackerUrl) => Secrets.GetValueOrDefault(trackerUrl);

    public void Set(Uri trackerUrl, string secret) => Secrets[trackerUrl] = secret;
}
