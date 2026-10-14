using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.StatusLine;

public sealed class CacheSessionTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.FromUnixTimeSeconds(1791997772);

    private static readonly CacheSession BeforeFirstResponse = new(0, null, false);

    [Fact]
    public void Next_remembers_zero_requests_before_the_first_response()
    {
        Assert.Equal(BeforeFirstResponse, CacheSession.Next(null, null));
    }

    [Fact]
    public void Next_keeps_the_memory_while_the_prompt_cache_is_absent()
    {
        var previous = new CacheSession(1, T, true);

        Assert.Equal(previous, CacheSession.Next(previous, null));
    }

    [Fact]
    public void Next_keeps_no_memory_without_a_request_count()
    {
        Assert.Null(CacheSession.Next(BeforeFirstResponse, new PromptCacheState(true, "5m", T.AddMinutes(5))));
    }

    [Fact]
    public void Next_takes_the_last_response_from_the_first_reading_after_the_request_count_changes()
    {
        var next = CacheSession.Next(BeforeFirstResponse, new PromptCacheState(true, "5m", T.AddMinutes(5), 1));

        Assert.Equal(new CacheSession(1, T, RenewalSeen: false), next);
    }

    [Fact]
    public void Next_does_not_know_the_last_response_without_a_previous_memory()
    {
        // Mid-session (first run after install, or a deleted file) the reading may already be a renewal,
        // whose expires_at minus the ttl is about now: no anchor until the request count changes.
        var cache = new PromptCacheState(true, "5m", T.AddMinutes(5), 4);
        var unknown = CacheSession.Next(null, cache);
        var renewedAgain = CacheSession.Next(unknown, cache with { ExpiresAt = T.AddMinutes(5).AddSeconds(30) });

        Assert.Equal(new CacheSession(4, null, false), unknown);
        Assert.Equal(unknown, renewedAgain);
        Assert.Equal("◷ cache 6m left (5m)", CacheTimerLine.Format(new StatusLineInput(cache), T.AddSeconds(-30), false, renewedAgain));
    }

    [Fact]
    public void Next_estimates_again_after_a_request_change_from_an_unknown_last_response()
    {
        var unknown = new CacheSession(4, null, false);

        var answered = CacheSession.Next(unknown, new PromptCacheState(true, "5m", T.AddMinutes(5), 5));
        var renewed = CacheSession.Next(answered, new PromptCacheState(true, "5m", T.AddMinutes(5).AddSeconds(30), 5));

        Assert.Equal(new CacheSession(5, T, true), renewed);
    }

    [Fact]
    public void Next_estimates_from_the_first_response_when_it_saw_the_session_before_it()
    {
        var first = CacheSession.Next(CacheSession.Next(null, null), new PromptCacheState(true, "5m", T.AddMinutes(5), 1));
        var cache = new PromptCacheState(true, "5m", T.AddMinutes(5).AddSeconds(30), 1);
        var renewed = CacheSession.Next(first, cache);

        Assert.Equal("◷ cache ~65m left (5m)", CacheTimerLine.Format(new StatusLineInput(cache), T.AddSeconds(30), false, renewed));
    }

    [Theory]
    [InlineData("1h", 3600)]
    [InlineData("30s", 30)]
    public void Next_subtracts_the_ttl_of_the_reading(string ttl, long ttlSeconds)
    {
        var next = CacheSession.Next(BeforeFirstResponse, new PromptCacheState(true, ttl, T.AddSeconds(ttlSeconds), 7));

        Assert.Equal(new CacheSession(7, T, false), next);
    }

    [Theory]
    [InlineData("5m", null)]
    [InlineData(null, 300L)]
    [InlineData("5d", 300L)]
    public void Next_does_not_know_the_last_response_without_an_expiry_and_a_ttl(string? ttl, long? expiresIn)
    {
        var expiresAt = expiresIn is { } seconds ? T.AddSeconds(seconds) : (DateTimeOffset?)null;

        var next = CacheSession.Next(BeforeFirstResponse, new PromptCacheState(true, ttl, expiresAt, 1));

        Assert.Equal(new CacheSession(1, null, false), next);
    }

    [Fact]
    public void Next_starts_over_when_the_request_count_changes()
    {
        var next = CacheSession.Next(new CacheSession(1, T, true), new PromptCacheState(true, "5m", T.AddMinutes(20), 2));

        Assert.Equal(new CacheSession(2, T.AddMinutes(15), false), next);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(6)]
    [InlineData(3300)]
    public void Next_sees_the_renewal_when_the_expiry_moves_forward_with_the_same_requests(long movedSeconds)
    {
        var next = CacheSession.Next(
            new CacheSession(3, T, false), new PromptCacheState(true, "5m", T.AddMinutes(5).AddSeconds(movedSeconds), 3));

        Assert.Equal(new CacheSession(3, T, true), next);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-30)]
    public void Next_ignores_an_expiry_that_moves_less_than_the_tolerance(long movedSeconds)
    {
        var previous = new CacheSession(3, T, false);

        var next = CacheSession.Next(previous, new PromptCacheState(true, "5m", T.AddMinutes(5).AddSeconds(movedSeconds), 3));

        Assert.Equal(previous, next);
    }

    [Theory]
    [InlineData("5m", null)]
    [InlineData(null, 600L)]
    public void Next_does_not_see_a_renewal_without_an_expiry_and_a_ttl(string? ttl, long? expiresIn)
    {
        var previous = new CacheSession(3, T, false);
        var expiresAt = expiresIn is { } seconds ? T.AddSeconds(seconds) : (DateTimeOffset?)null;

        Assert.Equal(previous, CacheSession.Next(previous, new PromptCacheState(true, ttl, expiresAt, 3)));
    }

    [Fact]
    public void Next_does_not_see_a_renewal_without_the_last_response()
    {
        var previous = new CacheSession(3, null, false);

        Assert.Equal(previous, CacheSession.Next(previous, new PromptCacheState(true, "5m", T.AddHours(1), 3)));
    }

    [Fact]
    public void Next_remembers_a_renewal_once_seen()
    {
        var previous = new CacheSession(3, T, true);

        Assert.Equal(previous, CacheSession.Next(previous, new PromptCacheState(false, "5m", null, 3)));
    }
}
