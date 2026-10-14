using System.Globalization;

namespace NexerAI.Core.StatusLine;

/// <summary>
/// The line <c>nexer-ai statusline</c> prints: how long the prompt cache stays warm. Claude Code re-runs the
/// command when a warm cache reaches its <c>expires_at</c>, so the switch to cold shows without a refresh.
/// </summary>
public static class CacheTimerLine
{
    /// <summary>Formats the cache of <paramref name="input"/> as seen at <paramref name="now"/>.</summary>
    /// <returns>
    /// <c>cache -</c> when the cache is unknown (unreadable input, or no response yet); <c>cache cold</c> when
    /// it is not warm, has no expiry or has expired; otherwise <c>cache {m}m left ({ttl})</c>, with the time
    /// left rounded up to whole minutes and the <c>(ttl)</c> part only when the ttl is a short token.
    /// </returns>
    public static string Format(StatusLineInput? input, DateTimeOffset now)
    {
        if (input?.PromptCache is not { } cache)
        {
            return "cache -";
        }

        if (!cache.Warm || cache.ExpiresAt is not { } expiresAt || expiresAt <= now)
        {
            return "cache cold";
        }

        // Whole ticks, so an exact minute is not pushed up by floating point error.
        var ticksLeft = (expiresAt - now).Ticks;
        var minutesLeft = ((ticksLeft - 1) / TimeSpan.TicksPerMinute) + 1;
        var ttl = IsShortToken(cache.Ttl) ? $" ({cache.Ttl})" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"cache {minutesLeft}m left{ttl}");
    }

    /// <summary>
    /// One to three ASCII digits and a unit, such as <c>5m</c> or <c>1h</c>. Any other ttl is dropped, so no
    /// input text other than this reaches the terminal (it could carry escape sequences).
    /// </summary>
    private static bool IsShortToken(string? ttl) =>
        ttl is { Length: >= 2 and <= 4 }
        && ttl[^1] is 's' or 'm' or 'h'
        && ttl.AsSpan(0, ttl.Length - 1).IndexOfAnyExceptInRange('0', '9') < 0;
}
