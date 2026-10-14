using NexerAI.Core.Ports;

namespace NexerAI.Core.StatusLine;

/// <summary>The two entry points of <c>nexer-ai statusline</c>: the status line run and the session end cleanup.</summary>
public static class StatusLineCommand
{
    /// <summary>
    /// How long a session's memory is kept without a write. <c>SessionEnd</c> is not documented to run when the
    /// terminal is closed, and after 65 idle minutes the cache is cold anyway.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);

    /// <summary>
    /// Sweeps stale memories, reads the status line JSON, updates the session's memory (also before the first
    /// response; written only when it changed, and only for an id accepted by <see cref="SessionId.IsFileName"/>)
    /// and returns the line.
    /// </summary>
    public static string Render(string json, DateTimeOffset now, bool color, ISessionCacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        store.SweepOlderThan(now - StaleAfter);
        var input = StatusLineInputParser.Parse(json);

        CacheSession? session = null;
        if (input is { SessionId: { } id } && SessionId.IsFileName(id))
        {
            var previous = store.Load(id);
            session = CacheSession.Next(previous, input.PromptCache);
            if (session is not null && session != previous)
            {
                store.Save(id, session);
            }
        }

        return CacheTimerLine.Format(input, now, color, session);
    }

    /// <summary>Reads the <c>SessionEnd</c> hook JSON on standard input and forgets that session.</summary>
    public static void EndSession(string json, ISessionCacheStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (StatusLineInputParser.Parse(json)?.SessionId is { } id && SessionId.IsFileName(id))
        {
            store.Delete(id);
        }
    }
}
