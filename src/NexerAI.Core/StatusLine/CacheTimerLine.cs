using System.Globalization;

namespace NexerAI.Core.StatusLine;

/// <summary>
/// The line <c>nexer-ai statusline</c> prints: how long the prompt cache stays warm. Claude Code re-runs the
/// command when a warm cache reaches its <c>expires_at</c>, so the switch to cold shows without a refresh.
/// </summary>
public static class CacheTimerLine
{
    private const string Prefix = "◷ ";
    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Gray = "\u001b[90m";
    private const string Reset = "\u001b[0m";

    private static readonly TimeSpan MinWarningWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxWarningWindow = TimeSpan.FromSeconds(300);

    /// <summary>How long Claude Code was seen renewing an idle cache after the last response (2.1.300).</summary>
    private static readonly TimeSpan RenewalPeriod = TimeSpan.FromMinutes(60);

    /// <summary>
    /// How long the estimate is shown after the last response without a renewal: renewals come every 30 seconds,
    /// so if none came by then, Claude Code stopped renewing and the exact countdown takes over.
    /// </summary>
    private static readonly TimeSpan RenewalGrace = TimeSpan.FromSeconds(75);

    /// <summary>Formats the cache of <paramref name="input"/> as seen at <paramref name="now"/>.</summary>
    /// <param name="input">The parsed status line input; null when it was unreadable.</param>
    /// <param name="now">The current time.</param>
    /// <param name="color">
    /// Whether to wrap the line in ANSI colors: green while warm, yellow within the warning window (20% of
    /// the ttl clamped to 60 to 300 seconds; 60 seconds when the ttl is unknown), gray when cold or unknown.
    /// </param>
    /// <param name="session">
    /// The session's memory for the same input (<see cref="CacheSession.Next"/>); null when there is none.
    /// </param>
    /// <returns>
    /// <c>◷ cache -</c> when the cache is unknown (unreadable input, or no response yet); <c>◷ cache cold</c>
    /// when it is not warm, has no expiry or has expired; otherwise <c>◷ cache {m}m left ({ttl})</c>, with the
    /// time left rounded up to whole minutes and the <c>(ttl)</c> part only when the ttl is a short token. When
    /// <paramref name="session"/> knows the last response, and for 60 minutes after it, the time left is
    /// estimated to the last response plus 60 minutes plus the ttl, never less than the exact time left, and
    /// shown as <c>◷ cache ~{m}m left ({ttl})</c>: estimate, then correct. It shows right after the response,
    /// when the cache is warm for at least one ttl anyway, so an early estimate cannot mislead; if no renewal
    /// was seen within 75 seconds of the response, the exact countdown shows until one is.
    /// </returns>
    public static string Format(StatusLineInput? input, DateTimeOffset now, bool color, CacheSession? session = null)
    {
        if (input?.PromptCache is not { } cache)
        {
            return Paint("cache -", Gray, color);
        }

        if (!cache.Warm || cache.ExpiresAt is not { } expiresAt || expiresAt <= now)
        {
            return Paint("cache cold", Gray, color);
        }

        var ttlLength = CacheTtl.Parse(cache.Ttl);
        var timeLeft = expiresAt - now;
        var estimated = false;
        if (session is { LastResponseAt: { } lastResponseAt }
            && ttlLength is { } length
            && now < lastResponseAt + RenewalPeriod
            && (session.RenewalSeen || now - lastResponseAt <= RenewalGrace))
        {
            var estimate = lastResponseAt + RenewalPeriod + length - now;
            timeLeft = estimate > timeLeft ? estimate : timeLeft;
            estimated = true;
        }

        // Whole ticks, so an exact minute is not pushed up by floating point error.
        var minutesLeft = ((timeLeft.Ticks - 1) / TimeSpan.TicksPerMinute) + 1;
        var ttl = ttlLength is null ? string.Empty : $" ({cache.Ttl})";
        var mark = estimated ? "~" : string.Empty;
        var text = string.Create(CultureInfo.InvariantCulture, $"cache {mark}{minutesLeft}m left{ttl}");
        return Paint(text, timeLeft <= WarningWindow(ttlLength) ? Yellow : Green, color);
    }

    private static string Paint(string text, string sgr, bool color) =>
        color ? sgr + Prefix + text + Reset : Prefix + text;

    private static TimeSpan WarningWindow(TimeSpan? ttl) =>
        ttl is { } length ? Clamp(length / 5) : MinWarningWindow;

    private static TimeSpan Clamp(TimeSpan window) =>
        window < MinWarningWindow ? MinWarningWindow : window > MaxWarningWindow ? MaxWarningWindow : window;
}
