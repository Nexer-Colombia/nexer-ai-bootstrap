using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.StatusLine;

/// <summary>
/// <c>before-first-response.json</c> and <c>warm-cache.json</c> in <c>Samples</c> were recorded from Claude Code
/// 2.1.299 on a Claude Team account and anonymized (identifiers, paths and names replaced); numbers and shapes
/// are as recorded. The other samples are written by hand in the recorded shape, for the cache cases the
/// recordings do not cover.
/// </summary>
public sealed class StatusLineInputParserTests
{
    [Fact]
    public void Parse_reads_the_input_recorded_before_the_first_response()
    {
        Assert.Equal(new StatusLineInput(null), Parse(Sample("before-first-response.json")));
    }

    [Fact]
    public void Parse_reads_the_input_recorded_with_a_warm_cache()
    {
        var input = Parse(Sample("warm-cache.json"));

        Assert.Equal(new PromptCacheState(true, "5m", At(1791998012)), input.PromptCache);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"prompt_cache\": }")]
    [InlineData("[]")]
    [InlineData("[{\"prompt_cache\": {\"warm\": true}}]")]
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
        Assert.Null(StatusLineInputParser.Parse("{\"prompt_cache\": {\"warm\": true, \"ttl\": \"\uD800\"}}"));
    }

    [Fact]
    public void Parse_reads_a_cold_prompt_cache_without_an_expiry()
    {
        var input = Parse("{\"prompt_cache\": {\"warm\": false, \"ttl\": \"1h\", \"expires_at\": null}}");

        Assert.Equal(new PromptCacheState(false, "1h", null), input.PromptCache);
    }

    [Theory]
    [InlineData("{\"warm\": true, \"ttl\": 300, \"expires_at\": \"soon\"}")]
    [InlineData("{\"warm\": true, \"ttl\": null, \"expires_at\": 1791998012.5}")]
    [InlineData("{\"warm\": true, \"expires_at\": 999999999999999}")]
    public void Parse_reads_prompt_cache_fields_of_the_wrong_type_as_unknown(string cache)
    {
        var input = Parse("{\"prompt_cache\": " + cache + "}");

        Assert.Equal(new PromptCacheState(true, null, null), input.PromptCache);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"prompt_cache\": null}")]
    [InlineData("{\"prompt_cache\": \"warm\"}")]
    [InlineData("{\"prompt_cache\": []}")]
    [InlineData("{\"prompt_cache\": {\"ttl\": \"5m\", \"expires_at\": 1791998012}}")]
    [InlineData("{\"prompt_cache\": {\"warm\": \"true\", \"ttl\": \"5m\"}}")]
    [InlineData("{\"prompt_cache\": {\"warm\": null, \"ttl\": \"5m\"}}")]
    public void Parse_reads_a_missing_null_or_mistyped_cache_as_unknown(string json)
    {
        // The cache needs a boolean warm flag; otherwise it is unknown.
        Assert.Equal(new StatusLineInput(null), Parse(json));
    }

    [Fact]
    public void Parse_ignores_unknown_fields()
    {
        var input = Parse("{\"future\": [1, 2], \"prompt_cache\": {\"warm\": false, \"hit_ratio\": 0.8}}");

        Assert.Equal(new StatusLineInput(new PromptCacheState(false, null, null)), input);
    }

    private static StatusLineInput Parse(string json) =>
        StatusLineInputParser.Parse(json) ?? throw new InvalidOperationException("The input was unreadable.");

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "StatusLine", "Samples", name));

    private static DateTimeOffset At(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
}
