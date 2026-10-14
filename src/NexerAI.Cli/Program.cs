using System.Reflection;
using System.Text;
using NexerAI.Core.StatusLine;

// Composition root. Commands (install, project, doctor, mcp-launch) arrive in later units.
if (args is ["--version"])
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    Console.Out.WriteLine($"nexer-ai {version}");
    return 0;
}

if (args is ["statusline"])
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

    // LF only: Claude Code shows each output line as a status line row.
    Console.Out.Write(CacheTimerLine.Format(StatusLineInputParser.Parse(json), DateTimeOffset.UtcNow) + "\n");
    return 0;
}

Console.Error.WriteLine("""
    Usage:
      nexer-ai --version
      nexer-ai statusline    Prints the prompt cache timer from Claude Code's status line JSON on stdin
    """);
return 2;
