using System.Reflection;
using System.Text;
using NexerAI.Adapters.StatusLine;
using NexerAI.Core.StatusLine;

// Composition root. Commands (install, project, doctor, mcp-launch) arrive in later units.
if (args is ["--version"])
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    Console.Out.WriteLine($"nexer-ai {version}");
    return 0;
}

if (args is ["statusline"] or ["statusline", "--session-end"])
{
    // A status line must always print something, so a failed read counts as unreadable input and exits 0.
    string json;
    try
    {
        using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        json = stdin.ReadToEnd();
    }
    catch (IOException)
    {
        json = string.Empty;
    }

    var store = new FileSessionCacheStore(FileSessionCacheStore.DefaultDirectory);
    if (args is [_, "--session-end"])
    {
        // SessionEnd hook: forget the session, print nothing.
        StatusLineCommand.EndSession(json, store);
        return 0;
    }

    // no-color.org: NO_COLOR present and not empty turns colors off.
    var color = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
    var line = StatusLineCommand.Render(json, DateTimeOffset.UtcNow, color, store);

    // UTF-8 bytes without a BOM, whatever the console code page, so the clock glyph survives Git Bash.
    // LF only: Claude Code shows each output line as a status line row.
    using var stdout = Console.OpenStandardOutput();
    stdout.Write(Encoding.UTF8.GetBytes(line + "\n"));
    return 0;
}

Console.Error.WriteLine("""
    Usage:
      nexer-ai --version
      nexer-ai statusline                  Prints the prompt cache timer from Claude Code's status line JSON on stdin
      nexer-ai statusline --session-end    SessionEnd hook: forgets the session named in the hook JSON on stdin
    """);
return 2;
