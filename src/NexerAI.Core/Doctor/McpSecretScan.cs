namespace NexerAI.Core.Doctor;

/// <summary>
/// Outcome of scanning the text of Claude Code's <c>~/.claude.json</c> for MCP server settings that hold
/// a literal secret. The cases below are the only ones: the text was scanned, or it is not valid JSON.
/// </summary>
public abstract record McpSecretScan
{
    private McpSecretScan()
    {
    }

    /// <summary>The text was scanned; <paramref name="Findings"/> is empty when nothing looks like a secret.</summary>
    public sealed record Scanned(IReadOnlyList<McpSecretFinding> Findings) : McpSecretScan;

    /// <summary>The text is not valid JSON, so nothing was scanned.</summary>
    public sealed record Unreadable : McpSecretScan;
}

/// <summary>
/// Where an MCP server is registered in <c>~/.claude.json</c>: at the top level (user scope) or under one
/// project path (local scope).
/// </summary>
public abstract record McpScope
{
    private McpScope()
    {
    }

    /// <summary>The top-level <c>mcpServers</c>, available in every project.</summary>
    public sealed record User : McpScope;

    /// <summary>The <c>mcpServers</c> of the project whose key in <c>projects</c> is <paramref name="Path"/>.</summary>
    public sealed record Project(string Path) : McpScope;
}

/// <summary>The part of an MCP server's configuration that holds the entry.</summary>
public enum McpSecretLocation
{
    /// <summary>The <c>env</c> object, environment variables of a stdio server.</summary>
    Env,

    /// <summary>The <c>headers</c> object, HTTP headers of a remote server.</summary>
    Header,
}

/// <summary>The rule that flagged an entry. When both rules match, <see cref="KeyName"/> is reported.</summary>
public enum SecretMatch
{
    /// <summary>The key name suggests a secret, for example <c>GITHUB_TOKEN</c> or <c>Authorization</c>.</summary>
    KeyName,

    /// <summary>The value has the shape of a known token, for example <c>ghp_...</c> or <c>Bearer ...</c>.</summary>
    ValueShape,
}

/// <summary>
/// An MCP server setting whose value looks like a literal secret. It names the entry, never the value:
/// there is deliberately no property that holds the value, its length or any part of it.
/// </summary>
public sealed record McpSecretFinding(
    McpScope Scope,
    string Server,
    McpSecretLocation Location,
    string Key,
    SecretMatch Match);
