namespace NexerAI.Core.StatusLine;

/// <summary>
/// What the status line remembers about one session between runs, to tell an idle renewal from a response
/// (design 4.5, guarded estimate). Observed on Claude Code 2.1.300 and undocumented: while a session is open
/// and idle, <c>expires_at</c> moves forward every 30 seconds with <c>requests</c> unchanged, for 60 minutes
/// after the last response.
/// </summary>
/// <param name="Requests"><c>prompt_cache.requests</c> when this memory was taken.</param>
/// <param name="LastResponseAt">
/// <c>expires_at</c> minus the ttl at the first reading after <c>requests</c> changed; null when that reading
/// had no expiry or no ttl, or when there was no memory before it.
/// </param>
/// <param name="RenewalSeen">Whether <c>expires_at</c> has since moved forward with the same <c>requests</c>.</param>
public sealed record CacheSession(long Requests, DateTimeOffset? LastResponseAt, bool RenewalSeen)
{
    /// <summary>How far past the last response plus the ttl <c>expires_at</c> must move to count as a renewal.</summary>
    public static readonly TimeSpan RenewalTolerance = TimeSpan.FromSeconds(5);

    /// <summary>Returns the memory after reading <paramref name="cache"/>.</summary>
    /// <param name="previous">The memory of the previous run; null when there is none.</param>
    /// <param name="cache">The prompt cache just read; null before the first response.</param>
    /// <returns>
    /// Without a cache, <paramref name="previous"/>, or zero requests when there was none, so the first
    /// response is a request change. Null with a cache but no request count. Without a previous memory the last
    /// response is unknown: the reading may already be a renewal, whose <c>expires_at</c> minus the ttl is about
    /// now. When <c>requests</c> changed, a fresh memory anchored at <c>expires_at</c> minus the ttl. Otherwise
    /// <paramref name="previous"/>, with the renewal seen once <c>expires_at</c> is later than the last response
    /// plus the ttl by more than <see cref="RenewalTolerance"/>.
    /// </returns>
    public static CacheSession? Next(CacheSession? previous, PromptCacheState? cache)
    {
        if (cache is null)
        {
            return previous ?? new CacheSession(0, null, RenewalSeen: false);
        }

        if (cache.Requests is not { } requests)
        {
            return null;
        }

        if (previous is null)
        {
            return new CacheSession(requests, null, RenewalSeen: false);
        }

        var ttl = CacheTtl.Parse(cache.Ttl);
        if (previous.Requests != requests)
        {
            return new CacheSession(requests, cache.ExpiresAt - ttl, RenewalSeen: false);
        }

        var renewed = !previous.RenewalSeen
            && previous.LastResponseAt is { } lastResponseAt
            && cache.ExpiresAt is { } expiresAt
            && ttl is { } length
            && expiresAt > lastResponseAt + length + RenewalTolerance;
        return renewed ? previous with { RenewalSeen = true } : previous;
    }
}
