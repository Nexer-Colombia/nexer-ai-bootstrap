using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Ports;

/// <summary>
/// The status line's memory of each session (design 4.5), one entry per session id. Callers pass only ids
/// accepted by <see cref="SessionId.IsFileName"/>. A status line must always print, so no member throws:
/// a failure reads as no memory and writes nothing.
/// </summary>
public interface ISessionCacheStore
{
    /// <summary>The stored memory of <paramref name="sessionId"/>, or null when there is none or it cannot be read.</summary>
    CacheSession? Load(string sessionId);

    /// <summary>Stores <paramref name="session"/> as the memory of <paramref name="sessionId"/>.</summary>
    void Save(string sessionId, CacheSession session);

    /// <summary>Forgets <paramref name="sessionId"/>.</summary>
    void Delete(string sessionId);

    /// <summary>Forgets every session whose memory was last written before <paramref name="cutoff"/>.</summary>
    void SweepOlderThan(DateTimeOffset cutoff);
}
