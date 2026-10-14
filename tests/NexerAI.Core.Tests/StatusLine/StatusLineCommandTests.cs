using NexerAI.Core.StatusLine;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.StatusLine;

public sealed class StatusLineCommandTests
{
    private const string Id = "s-1";

    private static readonly DateTimeOffset T = DateTimeOffset.FromUnixTimeSeconds(1791997772);

    private readonly FakeSessionCacheStore store = new();

    [Fact]
    public void Render_estimates_from_a_response_it_saw_the_session_before()
    {
        // Seen before the first response, so the first response at T anchors the estimate at once; the idle
        // renewal 30 s later keeps it.
        Assert.Equal("◷ cache -", Render(BeforeFirstResponse(Id), T.AddSeconds(-5)));
        Assert.Equal(new CacheSession(0, null, false), store.Sessions[Id]);
        Assert.Equal("◷ cache ~65m left (5m)", Render(Input(Id, requests: 1, expiresAt: T.AddMinutes(5)), T));
        Assert.Equal(
            "◷ cache ~65m left (5m)", Render(Input(Id, 1, T.AddMinutes(5).AddSeconds(30)), T.AddSeconds(30)));

        // A new response moves the anchor.
        var next = T.AddMinutes(2);
        Assert.Equal("◷ cache ~65m left (5m)", Render(Input(Id, 2, next.AddMinutes(5)), next));
        Assert.Equal(new CacheSession(2, next, false), store.Sessions[Id]);
    }

    [Fact]
    public void Render_shows_the_exact_countdown_when_no_renewal_came_75_seconds_after_the_response()
    {
        Render(BeforeFirstResponse(Id), T.AddSeconds(-5));
        Render(Input(Id, 1, T.AddMinutes(5)), T);

        Assert.Equal("◷ cache 4m left (5m)", Render(Input(Id, 1, T.AddMinutes(5)), T.AddSeconds(76)));
        Assert.Equal(
            "◷ cache ~64m left (5m)", Render(Input(Id, 1, T.AddMinutes(5).AddSeconds(90)), T.AddSeconds(90)));
    }

    [Fact]
    public void Render_stays_exact_when_it_first_sees_the_session_after_a_response()
    {
        // No memory: the reading may already be a renewal, so the last response is unknown.
        Assert.Equal("◷ cache 5m left (5m)", Render(Input(Id, 4, T.AddMinutes(5)), T));
        Assert.Equal("◷ cache 5m left (5m)", Render(Input(Id, 4, T.AddMinutes(5).AddSeconds(30)), T.AddSeconds(30)));
        Assert.Equal(new CacheSession(4, null, false), store.Sessions[Id]);

        // The next response anchors it.
        var next = T.AddMinutes(1);
        Render(Input(Id, 5, next.AddMinutes(5)), next);
        Assert.Equal(
            "◷ cache ~65m left (5m)", Render(Input(Id, 5, next.AddMinutes(5).AddSeconds(30)), next.AddSeconds(30)));
    }

    [Fact]
    public void Render_saves_only_when_the_session_memory_changes()
    {
        Render(BeforeFirstResponse(Id), T.AddSeconds(-5));
        Render(BeforeFirstResponse(Id), T.AddSeconds(-4));
        Render(Input(Id, 1, T.AddMinutes(5)), T);
        Render(Input(Id, 1, T.AddMinutes(5)), T.AddSeconds(10));
        Render(Input(Id, 1, T.AddMinutes(5).AddSeconds(30)), T.AddSeconds(30));
        Render(Input(Id, 1, T.AddMinutes(5).AddSeconds(60)), T.AddSeconds(60));

        Assert.Equal([Id, Id, Id], store.Saved);
        Assert.Equal(new CacheSession(1, T, true), store.Sessions[Id]);
    }

    [Theory]
    [InlineData("..\\\\x")]
    [InlineData("a/b")]
    [InlineData("")]
    public void Render_stores_nothing_for_a_session_id_that_is_not_a_plain_token(string sessionId)
    {
        var line = Render(Input(sessionId, 1, T.AddMinutes(5)), T);

        Assert.Equal("◷ cache 5m left (5m)", line);
        Assert.Empty(store.Loaded);
        Assert.Empty(store.Saved);
    }

    [Theory]
    [InlineData("{\"prompt_cache\": {\"warm\": true, \"ttl\": \"5m\", \"expires_at\": 1791998072, \"requests\": 1}}", "◷ cache 5m left (5m)")]
    [InlineData("not json", "◷ cache -")]
    public void Render_stores_nothing_without_a_readable_session_id(string json, string expected)
    {
        Assert.Equal(expected, StatusLineCommand.Render(json, T, color: false, store));
        Assert.Empty(store.Saved);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"session_id\": \"s-1\", \"prompt_cache\": {\"warm\": true, \"requests\": 1}}")]
    public void Render_sweeps_session_files_untouched_for_two_hours_on_every_run(string json)
    {
        StatusLineCommand.Render(json, T, color: false, store);

        Assert.Equal([T.AddHours(-2)], store.Sweeps);
    }

    [Fact]
    public void EndSession_deletes_the_memory_of_the_ended_session()
    {
        store.Sessions[Id] = new CacheSession(1, T, true);

        StatusLineCommand.EndSession("{\"session_id\": \"s-1\", \"hook_event_name\": \"SessionEnd\", \"reason\": \"other\"}", store);

        Assert.Equal([Id], store.Deleted);
        Assert.Empty(store.Sessions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"reason\": \"clear\"}")]
    [InlineData("{\"session_id\": \"..\\\\x\"}")]
    [InlineData("{\"session_id\": \"a/b\"}")]
    public void EndSession_deletes_nothing_without_a_plain_session_id(string json)
    {
        StatusLineCommand.EndSession(json, store);

        Assert.Empty(store.Deleted);
    }

    private string Render(string json, DateTimeOffset now) => StatusLineCommand.Render(json, now, color: false, store);

    private static string BeforeFirstResponse(string sessionId) => $$"""{"session_id": "{{sessionId}}"}""";

    private static string Input(string sessionId, long requests, DateTimeOffset expiresAt) =>
        $$$"""{"session_id": "{{{sessionId}}}", "prompt_cache": {"warm": true, "ttl": "5m", "expires_at": {{{expiresAt.ToUnixTimeSeconds()}}}, "requests": {{{requests}}}}}""";
}
