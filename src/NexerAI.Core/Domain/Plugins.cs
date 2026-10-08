namespace NexerAI.Core.Domain;

/// <summary>Claude Code scope used by the bootstrap for plugins and MCP servers.</summary>
public enum PluginScope
{
    /// <summary>All projects of the current user.</summary>
    User,

    /// <summary>The current project path only, not committed to the repository.</summary>
    Local,
}

/// <summary>A plugin as reported by the agent.</summary>
public sealed record InstalledPlugin(string Name, string Version, PluginScope Scope);

/// <summary>A plugin to install, with its non-interactive configuration values.</summary>
public sealed record PluginInstallRequest(string Name, PluginScope Scope, IReadOnlyDictionary<string, string> Config);

/// <summary>A stdio MCP server to register; secrets are never part of it.</summary>
public sealed record McpServerRegistration(string Name, PluginScope Scope, string Command, IReadOnlyList<string> Arguments);

/// <summary>A GitHub issue to open, for example a profile request.</summary>
public sealed record IssueDraft(string Title, string Body, IReadOnlyList<string> Labels);
