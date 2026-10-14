using NexerAI.Core.Ports;
using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeSessionCacheStore : ISessionCacheStore
{
    public Dictionary<string, CacheSession> Sessions { get; } = new(StringComparer.Ordinal);

    public List<string> Loaded { get; } = [];

    public List<string> Saved { get; } = [];

    public List<string> Deleted { get; } = [];

    public List<DateTimeOffset> Sweeps { get; } = [];

    public CacheSession? Load(string sessionId)
    {
        Loaded.Add(sessionId);
        return Sessions.GetValueOrDefault(sessionId);
    }

    public void Save(string sessionId, CacheSession session)
    {
        Saved.Add(sessionId);
        Sessions[sessionId] = session;
    }

    public void Delete(string sessionId)
    {
        Deleted.Add(sessionId);
        Sessions.Remove(sessionId);
    }

    public void SweepOlderThan(DateTimeOffset cutoff) => Sweeps.Add(cutoff);
}
