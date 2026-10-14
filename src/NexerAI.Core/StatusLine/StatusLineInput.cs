namespace NexerAI.Core.StatusLine;

/// <summary>
/// The part of the JSON that Claude Code pipes to a status line command which <c>nexer-ai statusline</c>
/// shows. Every value is <see langword="null"/> when Claude Code did not send it or sent it in another shape.
/// </summary>
/// <param name="ContextUsedPercentage"><c>context_window.used_percentage</c>; null before the first response.</param>
/// <param name="PromptCache"><c>prompt_cache</c>.</param>
/// <param name="FiveHourLimit"><c>rate_limits.five_hour</c>, the plan's 5-hour limit.</param>
/// <param name="SevenDayLimit"><c>rate_limits.seven_day</c>, the plan's weekly limit.</param>
/// <param name="CostUsd"><c>cost.total_cost_usd</c>, the session's estimated cost.</param>
/// <param name="EffortLevel"><c>effort.level</c>, for example <c>high</c>, as sent.</param>
public sealed record StatusLineInput(
    double? ContextUsedPercentage,
    PromptCacheState? PromptCache,
    RateLimitWindow? FiveHourLimit,
    RateLimitWindow? SevenDayLimit,
    decimal? CostUsd,
    string? EffortLevel);

/// <summary>The prompt cache of the session; only known when <c>prompt_cache.warm</c> is a boolean.</summary>
/// <param name="Warm"><c>prompt_cache.warm</c>.</param>
/// <param name="Ttl"><c>prompt_cache.ttl</c>, for example <c>5m</c> or <c>1h</c>, as sent.</param>
/// <param name="ExpiresAt"><c>prompt_cache.expires_at</c>; null when the cache is cold.</param>
public sealed record PromptCacheState(bool Warm, string? Ttl, DateTimeOffset? ExpiresAt);

/// <summary>One plan limit window; only known when its <c>used_percentage</c> is a number.</summary>
/// <param name="UsedPercentage"><c>used_percentage</c>, from 0 to 100.</param>
/// <param name="ResetsAt"><c>resets_at</c>.</param>
public sealed record RateLimitWindow(double UsedPercentage, DateTimeOffset? ResetsAt);
