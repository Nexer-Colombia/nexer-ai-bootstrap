namespace NexerAI.Core.StatusLine;

/// <summary>
/// The part of the JSON that Claude Code pipes to a status line command which <c>nexer-ai statusline</c>
/// shows: only the prompt cache, the one value the mods API does not expose (the Nexer mod draws the other
/// metrics). Every value is <see langword="null"/> when Claude Code did not send it or sent it in another shape.
/// </summary>
/// <param name="PromptCache"><c>prompt_cache</c>; absent until the main conversation's first API response.</param>
/// <param name="SessionId">
/// <c>session_id</c>, as sent; it keys the per-session memory of the estimate (see <see cref="CacheSession"/>).
/// </param>
public sealed record StatusLineInput(PromptCacheState? PromptCache, string? SessionId = null);

/// <summary>The prompt cache of the session; only known when <c>prompt_cache.warm</c> is a boolean.</summary>
/// <param name="Warm"><c>prompt_cache.warm</c>.</param>
/// <param name="Ttl"><c>prompt_cache.ttl</c>, for example <c>5m</c> or <c>1h</c>, as sent.</param>
/// <param name="ExpiresAt"><c>prompt_cache.expires_at</c>; null when the last response reported no cache tokens.</param>
/// <param name="Requests"><c>prompt_cache.requests</c>, the API requests seen so far; changes on every response.</param>
public sealed record PromptCacheState(bool Warm, string? Ttl, DateTimeOffset? ExpiresAt, long? Requests = null);
