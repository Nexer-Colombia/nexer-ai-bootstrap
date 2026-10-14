using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.StatusLine;

public sealed class CacheTimerLineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1791997772);

    private const string Green = "\u001b[32m";
    private const string Yellow = "\u001b[33m";
    private const string Gray = "\u001b[90m";
    private const string Reset = "\u001b[0m";

    [Theory]
    [InlineData("before-first-response.json", 1791997772, "◷ cache -")]
    [InlineData("warm-cache.json", 1791997772, "◷ cache 4m left (5m)")]
    [InlineData("warm-cache-1h.json", 1791998492, "◷ cache 52m left (1h)")]
    [InlineData("cold-cache-no-expiry.json", 1791997772, "◷ cache cold")]
    [InlineData("expired-cache.json", 1791998013, "◷ cache cold")]
    public void Format_shows_the_cache_of_each_sample(string sample, long nowUnixSeconds, string expected)
    {
        var input = StatusLineInputParser.Parse(Sample(sample));

        var line = CacheTimerLine.Format(input, DateTimeOffset.FromUnixTimeSeconds(nowUnixSeconds), color: false);

        Assert.Equal(expected, line);
    }

    [Fact]
    public void Format_shows_an_unknown_cache_when_the_input_was_unreadable()
    {
        Assert.Equal("◷ cache -", CacheTimerLine.Format(null, Now, color: false));
    }

    [Fact]
    public void Format_shows_an_unknown_cache_before_the_first_response()
    {
        Assert.Equal("◷ cache -", CacheTimerLine.Format(new StatusLineInput(null), Now, color: false));
    }

    [Theory]
    [InlineData("5m", 60, Yellow, "1m left (5m)")]
    [InlineData("5m", 61, Green, "2m left (5m)")]
    [InlineData("1h", 300, Yellow, "5m left (1h)")]
    [InlineData("1h", 301, Green, "6m left (1h)")]
    [InlineData("10m", 120, Yellow, "2m left (10m)")]
    [InlineData("10m", 121, Green, "3m left (10m)")]
    [InlineData("30s", 60, Yellow, "1m left (30s)")]
    [InlineData("30s", 61, Green, "2m left (30s)")]
    [InlineData("999h", 300, Yellow, "5m left (999h)")]
    [InlineData("999h", 301, Green, "6m left (999h)")]
    [InlineData(null, 60, Yellow, "1m left")]
    [InlineData(null, 61, Green, "2m left")]
    [InlineData("5d", 60, Yellow, "1m left")]
    [InlineData("5d", 61, Green, "2m left")]
    public void Format_turns_a_warm_cache_yellow_within_its_warning_window(
        string? ttl, long secondsLeft, string color, string timeLeft)
    {
        // Window: 20% of the ttl, clamped to [60 s, 300 s]; 60 s when the ttl is unknown.
        var line = CacheTimerLine.Format(
            new StatusLineInput(new PromptCacheState(true, ttl, Now.AddSeconds(secondsLeft))), Now, color: true);

        Assert.Equal($"{color}◷ cache {timeLeft}{Reset}", line);
    }

    [Theory]
    [InlineData(90, Yellow)]
    [InlineData(91, Green)]
    public void Format_compares_the_exact_time_left_with_the_window_not_the_rounded_minutes(
        long secondsLeft, string color)
    {
        // A 450 s ttl has a 90 s window: both sides round up to 2 minutes but take different colors.
        var line = CacheTimerLine.Format(
            new StatusLineInput(new PromptCacheState(true, "450s", Now.AddSeconds(secondsLeft))), Now, color: true);

        Assert.Equal($"{color}◷ cache 2m left (450s){Reset}", line);
    }

    [Theory]
    [InlineData(false, "5m", 300L, "◷ cache cold")]
    [InlineData(true, "5m", null, "◷ cache cold")]
    [InlineData(true, "5m", 0L, "◷ cache cold")]
    public void Format_shows_a_cold_cache_in_gray(bool warm, string ttl, long? expiresInSeconds, string text)
    {
        var expiresAt = expiresInSeconds is { } seconds ? Now.AddSeconds(seconds) : (DateTimeOffset?)null;

        var line = CacheTimerLine.Format(new StatusLineInput(new PromptCacheState(warm, ttl, expiresAt)), Now, color: true);

        Assert.Equal($"{Gray}{text}{Reset}", line);
    }

    [Fact]
    public void Format_shows_an_unknown_cache_in_gray()
    {
        Assert.Equal($"{Gray}◷ cache -{Reset}", CacheTimerLine.Format(null, Now, color: true));
    }

    [Theory]
    [InlineData(true, 30L)]
    [InlineData(true, 600L)]
    [InlineData(false, 600L)]
    public void Format_writes_no_escape_codes_without_color(bool warm, long secondsLeft)
    {
        var line = CacheTimerLine.Format(
            new StatusLineInput(new PromptCacheState(warm, "5m", Now.AddSeconds(secondsLeft))), Now, color: false);

        Assert.StartsWith("◷ cache ", line, StringComparison.Ordinal);
        Assert.DoesNotContain('\u001b', line);
    }

    [Theory]
    [InlineData(false, 300L)]
    [InlineData(true, null)]
    [InlineData(true, 0L)]
    [InlineData(true, -1L)]
    [InlineData(true, -3600L)]
    public void Format_shows_a_cold_cache_unless_it_is_warm_with_a_future_expiry(bool warm, long? expiresInSeconds)
    {
        var expiresAt = expiresInSeconds is { } seconds ? Now.AddSeconds(seconds) : (DateTimeOffset?)null;

        Assert.Equal("◷ cache cold", Format(new PromptCacheState(warm, "5m", expiresAt)));
    }

    [Theory]
    [InlineData(1, "1m")]
    [InlineData(59, "1m")]
    [InlineData(60, "1m")]
    [InlineData(61, "2m")]
    [InlineData(240, "4m")]
    [InlineData(241, "5m")]
    [InlineData(300, "5m")]
    [InlineData(3120, "52m")]
    [InlineData(3600, "60m")]
    public void Format_rounds_the_time_left_up_to_whole_minutes(long secondsLeft, string expected)
    {
        var line = Format(new PromptCacheState(true, "5m", Now.AddSeconds(secondsLeft)));

        Assert.Equal($"◷ cache {expected} left (5m)", line);
    }

    [Fact]
    public void Format_rounds_up_a_fraction_of_a_second_left()
    {
        var line = CacheTimerLine.Format(
            new StatusLineInput(new PromptCacheState(true, "5m", Now)), Now.AddMilliseconds(-1), color: false);

        Assert.Equal("◷ cache 1m left (5m)", line);
    }

    [Theory]
    [InlineData("5m")]
    [InlineData("1h")]
    [InlineData("30s")]
    [InlineData("999h")]
    public void Format_shows_a_short_ttl(string ttl)
    {
        Assert.Equal($"◷ cache 4m left ({ttl})", Format(new PromptCacheState(true, ttl, Now.AddMinutes(4))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("m")]
    [InlineData("1000m")]
    [InlineData("5M")]
    [InlineData("5d")]
    [InlineData("5 m")]
    [InlineData(" 5m")]
    [InlineData("5m\n")]
    [InlineData("-5m")]
    [InlineData("١m")]
    [InlineData("\u001b[2J5m")]
    public void Format_omits_a_ttl_that_is_not_a_short_token(string? ttl)
    {
        // The ttl is the only input text that reaches the terminal, so anything else is dropped.
        Assert.Equal("◷ cache 4m left", Format(new PromptCacheState(true, ttl, Now.AddMinutes(4))));
    }

    [Theory]
    [InlineData(-10 * 60, 290, "◷ cache ~55m left (5m)")]
    [InlineData(-1, 300, "◷ cache ~65m left (5m)")]
    [InlineData(-60 * 60 + 1, 300, "◷ cache ~6m left (5m)")]
    [InlineData(-59 * 60, 600, "◷ cache ~10m left (5m)")]
    public void Format_estimates_until_an_hour_after_the_last_response_plus_the_ttl_once_renewal_was_seen(
        long lastResponseIn, long expiresIn, string expected)
    {
        // The estimate never shows less than the exact expires_at countdown.
        var session = new CacheSession(3, Now.AddSeconds(lastResponseIn), RenewalSeen: true);

        var line = CacheTimerLine.Format(Warm("5m", expiresIn), Now, color: false, session);

        Assert.Equal(expected, line);
    }

    [Theory]
    [InlineData(0, 300, "◷ cache ~65m left (5m)")]
    [InlineData(74, 300, "◷ cache ~64m left (5m)")]
    [InlineData(75, 300, "◷ cache ~64m left (5m)")]
    [InlineData(76, 300, "◷ cache 4m left (5m)")]
    [InlineData(240, 300, "◷ cache 1m left (5m)")]
    public void Format_estimates_right_after_the_response_and_corrects_itself_after_75_seconds_without_a_renewal(
        long secondsSinceResponse, long expiresAfterResponse, string expected)
    {
        // Right after a response the cache is warm for at least one ttl, so an early estimate cannot mislead.
        var session = new CacheSession(3, Now, RenewalSeen: false);
        var cache = new PromptCacheState(true, "5m", Now.AddSeconds(expiresAfterResponse), 3);

        var line = CacheTimerLine.Format(new StatusLineInput(cache), Now.AddSeconds(secondsSinceResponse), false, session);

        Assert.Equal(expected, line);
    }

    [Fact]
    public void Format_keeps_the_estimate_after_75_seconds_once_a_renewal_was_seen()
    {
        var session = new CacheSession(3, Now, RenewalSeen: true);

        var line = CacheTimerLine.Format(Warm("5m", 300 - 76), Now.AddSeconds(76), false, session);

        Assert.Equal("◷ cache ~64m left (5m)", line);
    }

    [Fact]
    public void Format_brings_the_estimate_back_when_a_renewal_arrives_after_75_seconds()
    {
        var anchored = new CacheSession(3, Now, false);
        var later = Now.AddSeconds(120);
        var renewedCache = new PromptCacheState(true, "5m", later.AddMinutes(5), 3);

        var exact = CacheTimerLine.Format(Warm("5m", 300), later, false, anchored);
        var estimate = CacheTimerLine.Format(
            new StatusLineInput(renewedCache), later, false, CacheSession.Next(anchored, renewedCache));

        Assert.Equal("◷ cache 3m left (5m)", exact);
        Assert.Equal("◷ cache ~63m left (5m)", estimate);
    }

    [Fact]
    public void Format_colors_the_estimate_by_the_warning_window()
    {
        var green = new CacheSession(3, Now.AddMinutes(-10), true);
        var yellow = new CacheSession(3, Now.AddMinutes(-60).AddSeconds(10), true);

        Assert.Equal($"{Green}◷ cache ~55m left (5m){Reset}", CacheTimerLine.Format(Warm("5m", 290), Now, color: true, green));
        Assert.Equal($"{Yellow}◷ cache ~1m left (30s){Reset}", CacheTimerLine.Format(Warm("30s", 20), Now, color: true, yellow));
    }

    [Theory]
    [InlineData(false, -600L, "5m", 290L)]
    [InlineData(true, null, "5m", 290L)]
    [InlineData(true, -3600L, "5m", 290L)]
    [InlineData(true, -4000L, "5m", 290L)]
    [InlineData(true, -600L, null, 290L)]
    [InlineData(true, -600L, "5d", 290L)]
    public void Format_shows_the_exact_countdown_when_the_estimate_does_not_apply(
        bool renewalSeen, long? lastResponseIn, string? ttl, long expiresIn)
    {
        var lastResponseAt = lastResponseIn is { } seconds ? Now.AddSeconds(seconds) : (DateTimeOffset?)null;
        var session = new CacheSession(3, lastResponseAt, renewalSeen);

        var line = CacheTimerLine.Format(Warm(ttl, expiresIn), Now, color: false, session);

        Assert.Equal(ttl == "5m" ? "◷ cache 5m left (5m)" : "◷ cache 5m left", line);
    }

    [Theory]
    [InlineData(false, 290L)]
    [InlineData(true, null)]
    [InlineData(true, 0L)]
    public void Format_shows_a_cold_cache_even_with_a_renewal_seen(bool warm, long? expiresIn)
    {
        var expiresAt = expiresIn is { } seconds ? Now.AddSeconds(seconds) : (DateTimeOffset?)null;
        var session = new CacheSession(3, Now.AddMinutes(-10), true);

        var line = CacheTimerLine.Format(
            new StatusLineInput(new PromptCacheState(warm, "5m", expiresAt, 3)), Now, color: false, session);

        Assert.Equal("◷ cache cold", line);
    }

    private static StatusLineInput Warm(string? ttl, long expiresIn) =>
        new(new PromptCacheState(true, ttl, Now.AddSeconds(expiresIn), 3));

    private static string Format(PromptCacheState cache) =>
        CacheTimerLine.Format(new StatusLineInput(cache), Now, color: false);

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StatusLine", "Samples", name));
}
