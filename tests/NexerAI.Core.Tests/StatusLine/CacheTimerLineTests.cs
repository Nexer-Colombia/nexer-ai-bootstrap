using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.StatusLine;

public sealed class CacheTimerLineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1791997772);

    [Theory]
    [InlineData("before-first-response.json", 1791997772, "cache -")]
    [InlineData("warm-cache.json", 1791997772, "cache 4m left (5m)")]
    [InlineData("warm-cache-1h.json", 1791998492, "cache 52m left (1h)")]
    [InlineData("cold-cache-no-expiry.json", 1791997772, "cache cold")]
    [InlineData("expired-cache.json", 1791998013, "cache cold")]
    public void Format_shows_the_cache_of_each_sample(string sample, long nowUnixSeconds, string expected)
    {
        var input = StatusLineInputParser.Parse(Sample(sample));

        Assert.Equal(expected, CacheTimerLine.Format(input, DateTimeOffset.FromUnixTimeSeconds(nowUnixSeconds)));
    }

    [Fact]
    public void Format_shows_an_unknown_cache_when_the_input_was_unreadable()
    {
        Assert.Equal("cache -", CacheTimerLine.Format(null, Now));
    }

    [Fact]
    public void Format_shows_an_unknown_cache_before_the_first_response()
    {
        Assert.Equal("cache -", CacheTimerLine.Format(new StatusLineInput(null), Now));
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

        Assert.Equal("cache cold", Format(new PromptCacheState(warm, "5m", expiresAt)));
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

        Assert.Equal($"cache {expected} left (5m)", line);
    }

    [Fact]
    public void Format_rounds_up_a_fraction_of_a_second_left()
    {
        var line = CacheTimerLine.Format(
            new StatusLineInput(new PromptCacheState(true, "5m", Now)), Now.AddMilliseconds(-1));

        Assert.Equal("cache 1m left (5m)", line);
    }

    [Theory]
    [InlineData("5m")]
    [InlineData("1h")]
    [InlineData("30s")]
    [InlineData("999h")]
    public void Format_shows_a_short_ttl(string ttl)
    {
        Assert.Equal($"cache 4m left ({ttl})", Format(new PromptCacheState(true, ttl, Now.AddMinutes(4))));
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
        Assert.Equal("cache 4m left", Format(new PromptCacheState(true, ttl, Now.AddMinutes(4))));
    }

    private static string Format(PromptCacheState cache) => CacheTimerLine.Format(new StatusLineInput(cache), Now);

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StatusLine", "Samples", name));
}
