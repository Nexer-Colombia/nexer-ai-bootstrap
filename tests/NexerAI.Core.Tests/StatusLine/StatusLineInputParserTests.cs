using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.StatusLine;

/// <summary>
/// The files in <c>Samples</c> were recorded from Claude Code 2.1.299 on a Claude Team account and
/// anonymized (identifiers, paths and names replaced); numbers and shapes are as recorded.
/// </summary>
public sealed class StatusLineInputParserTests
{
    [Fact]
    public void Parse_reads_the_input_recorded_before_the_first_response()
    {
        var input = Parse(Sample("before-first-response.json"));

        Assert.Null(input.ContextUsedPercentage);
        Assert.Null(input.PromptCache);
        Assert.Equal(new RateLimitWindow(17, At(1792007400)), input.FiveHourLimit);
        Assert.Equal(new RateLimitWindow(0, At(1792602000)), input.SevenDayLimit);
        Assert.Equal(0m, input.CostUsd);
        Assert.Equal("high", input.EffortLevel);
    }

    [Fact]
    public void Parse_reads_the_input_recorded_with_a_warm_cache()
    {
        var input = Parse(Sample("warm-cache.json"));

        Assert.Equal(7, input.ContextUsedPercentage);
        Assert.Equal(new PromptCacheState(true, "5m", At(1791998012)), input.PromptCache);
        Assert.Equal(new RateLimitWindow(18, At(1792007400)), input.FiveHourLimit);
        Assert.Equal(new RateLimitWindow(0, At(1792602000)), input.SevenDayLimit);
        Assert.Equal(0.2744063m, input.CostUsd);
        Assert.Equal("high", input.EffortLevel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"cost\": }")]
    [InlineData("[]")]
    [InlineData("[{\"cost\": {\"total_cost_usd\": 1}}]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void Parse_returns_null_for_text_that_is_not_a_json_object(string json)
    {
        Assert.Null(StatusLineInputParser.Parse(json));
    }

    [Fact]
    public void Parse_returns_null_for_text_that_cannot_be_encoded_as_utf8()
    {
        Assert.Null(StatusLineInputParser.Parse("{\"effort\": {\"level\": \"\uD800\"}}"));
    }

    [Fact]
    public void Parse_reads_each_rate_limit_window_independently()
    {
        var input = Parse("{\"rate_limits\": {\"seven_day\": {\"used_percentage\": 42, \"resets_at\": 1792602000}}}");

        Assert.Null(input.FiveHourLimit);
        Assert.Equal(new RateLimitWindow(42, At(1792602000)), input.SevenDayLimit);
    }

    [Theory]
    [InlineData("{\"used_percentage\": 18}")]
    [InlineData("{\"used_percentage\": 18, \"resets_at\": null}")]
    [InlineData("{\"used_percentage\": 18, \"resets_at\": \"tomorrow\"}")]
    [InlineData("{\"used_percentage\": 18, \"resets_at\": 1792007400.5}")]
    [InlineData("{\"used_percentage\": 18, \"resets_at\": 999999999999999}")]
    public void Parse_keeps_a_window_percentage_when_its_reset_time_is_unusable(string window)
    {
        var input = Parse("{\"rate_limits\": {\"five_hour\": " + window + "}}");

        Assert.Equal(new RateLimitWindow(18, null), input.FiveHourLimit);
    }

    [Fact]
    public void Parse_reads_a_cold_prompt_cache_without_an_expiry()
    {
        var input = Parse("{\"prompt_cache\": {\"warm\": false, \"ttl\": \"1h\", \"expires_at\": null}}");

        Assert.Equal(new PromptCacheState(false, "1h", null), input.PromptCache);
    }

    [Fact]
    public void Parse_reads_prompt_cache_fields_of_the_wrong_type_as_unknown()
    {
        var input = Parse("{\"prompt_cache\": {\"warm\": true, \"ttl\": 300, \"expires_at\": \"soon\"}}");

        Assert.Equal(new PromptCacheState(true, null, null), input.PromptCache);
    }

    [Theory]
    [InlineData("7", 7.0)]
    [InlineData("7.5", 7.5)]
    [InlineData("0", 0.0)]
    [InlineData("100", 100.0)]
    public void Parse_reads_integer_and_decimal_percentages(string number, double expected)
    {
        var input = Parse(
            "{\"context_window\": {\"used_percentage\": " + number + "},"
            + " \"rate_limits\": {\"five_hour\": {\"used_percentage\": " + number + ", \"resets_at\": 1792007400}}}");

        Assert.Equal(expected, input.ContextUsedPercentage);
        Assert.Equal(expected, input.FiveHourLimit?.UsedPercentage);
    }

    [Fact]
    public void Parse_reads_only_the_field_with_the_wrong_type_as_unknown()
    {
        var input = Parse(
            """
            {
              "context_window": {"used_percentage": "7"},
              "cost": {"total_cost_usd": "0.27"},
              "effort": "high",
              "rate_limits": {"five_hour": {"used_percentage": 18, "resets_at": 1792007400}}
            }
            """);

        Assert.Null(input.ContextUsedPercentage);
        Assert.Null(input.CostUsd);
        Assert.Null(input.EffortLevel);
        Assert.Equal(new RateLimitWindow(18, At(1792007400)), input.FiveHourLimit);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"context_window\": null, \"prompt_cache\": null, \"rate_limits\": null, \"cost\": null, \"effort\": null}")]
    [InlineData("{\"context_window\": [], \"prompt_cache\": \"warm\", \"rate_limits\": \"none\", \"cost\": 1, \"effort\": {}}")]
    [InlineData("{\"context_window\": {\"used_percentage\": null}, \"cost\": {\"total_cost_usd\": null}, \"effort\": {\"level\": 3}}")]
    [InlineData("{\"rate_limits\": {}}")]
    [InlineData("{\"rate_limits\": {\"five_hour\": null, \"seven_day\": []}}")]
    [InlineData("{\"rate_limits\": {\"five_hour\": {\"resets_at\": 1792007400}}}")]
    [InlineData("{\"rate_limits\": {\"five_hour\": {\"used_percentage\": \"18\", \"resets_at\": 1792007400}}}")]
    [InlineData("{\"prompt_cache\": {\"ttl\": \"5m\", \"expires_at\": 1791998012}}")]
    [InlineData("{\"prompt_cache\": {\"warm\": \"true\", \"ttl\": \"5m\"}}")]
    public void Parse_reads_missing_null_or_mistyped_values_as_unknown(string json)
    {
        // A window needs a numeric used_percentage and the cache a boolean warm flag; otherwise they are unknown.
        Assert.Equal(new StatusLineInput(null, null, null, null, null, null), Parse(json));
    }

    [Fact]
    public void Parse_ignores_unknown_fields()
    {
        var input = Parse("{\"spend_limit\": {\"used\": 3}, \"future\": [1, 2], \"effort\": {\"level\": \"xhigh\", \"extra\": true}}");

        Assert.Equal(new StatusLineInput(null, null, null, null, null, "xhigh"), input);
    }

    private static StatusLineInput Parse(string json) =>
        StatusLineInputParser.Parse(json) ?? throw new InvalidOperationException("The input was unreadable.");

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StatusLine", "Samples", name));

    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
}
