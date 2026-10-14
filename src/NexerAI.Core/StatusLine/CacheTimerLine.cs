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

    /// <summary>Formats the cache of <paramref name="input"/> as seen at <paramref name="now"/>.</summary>
    /// <param name="input">The parsed status line input; null when it was unreadable.</param>
    /// <param name="now">The current time.</param>
    /// <param name="color">
    /// Whether to wrap the line in ANSI colors: green while warm, yellow within the warning window (20% of
    /// the ttl clamped to 60 to 300 seconds; 60 seconds when the ttl is unknown), gray when cold or unknown.
    /// </param>
    /// <returns>
    /// <c>◷ cache -</c> when the cache is unknown (unreadable input, or no response yet); <c>◷ cache cold</c>
    /// when it is not warm, has no expiry or has expired; otherwise <c>◷ cache {m}m left ({ttl})</c>, with the
    /// time left rounded up to whole minutes and the <c>(ttl)</c> part only when the ttl is a short token.
    /// </returns>
    public static string Format(StatusLineInput? input, DateTimeOffset now, bool color)
    {
        if (input?.PromptCache is not { } cache)
        {
            return Paint("cache -", Gray, color);
        }

        if (!cache.Warm || cache.ExpiresAt is not { } expiresAt || expiresAt <= now)
        {
            return Paint("cache cold", Gray, color);
        }

        // Whole ticks, so an exact minute is not pushed up by floating point error.
        var timeLeft = expiresAt - now;
        var minutesLeft = ((timeLeft.Ticks - 1) / TimeSpan.TicksPerMinute) + 1;
        var ttlLength = ParseShortToken(cache.Ttl);
        var ttl = ttlLength is null ? string.Empty : $" ({cache.Ttl})";
        var text = string.Create(CultureInfo.InvariantCulture, $"cache {minutesLeft}m left{ttl}");
        return Paint(text, timeLeft <= WarningWindow(ttlLength) ? Yellow : Green, color);
    }

    private static string Paint(string text, string sgr, bool color) =>
        color ? sgr + Prefix + text + Reset : Prefix + text;

    private static TimeSpan WarningWindow(TimeSpan? ttl) =>
        ttl is { } length ? Clamp(length / 5) : MinWarningWindow;

    private static TimeSpan Clamp(TimeSpan window) =>
        window < MinWarningWindow ? MinWarningWindow : window > MaxWarningWindow ? MaxWarningWindow : window;

    /// <summary>
    /// The length of a ttl made of one to three ASCII digits and a unit, such as <c>5m</c> or <c>1h</c>; null
    /// for any other ttl, which is then dropped, so no input text other than this reaches the terminal (it
    /// could carry escape sequences).
    /// </summary>
    private static TimeSpan? ParseShortToken(string? ttl)
    {
        if (ttl is not { Length: >= 2 and <= 4 })
        {
            return null;
        }

        var digits = ttl.AsSpan(0, ttl.Length - 1);
        if (digits.IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            return null;
        }

        var amount = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return ttl[^1] switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            _ => null,
        };
    }
}
