namespace NexerAI.Core.Domain;

/// <summary>Claude Code scope used by the bootstrap for plugins and MCP servers.</summary>
public enum PluginScope
{
    /// <summary>All projects of the current user.</summary>
    User,

    /// <summary>The current project path only, not committed to the repository.</summary>
    Local,
}

/// <summary>A plugin as reported by the agent; Claude Code identifies it as <c>Name@Marketplace</c>.</summary>
/// <param name="Enabled">
/// Whether the agent loads it. A disabled plugin still counts as installed and is left alone (design 4.4):
/// installing it again would enable it, and <c>doctor</c> reports it instead.
/// </param>
public sealed record InstalledPlugin(string Name, string Marketplace, string Version, PluginScope Scope, bool Enabled);

/// <summary>
/// A plugin to install from a marketplace (the channel name, see <see cref="Channel.Name"/>),
/// with its non-interactive configuration values.
/// </summary>
/// <param name="Config">
/// Non-secret <c>userConfig</c> values only: they are passed on the command line. Secrets, such as the Engram token,
/// go through <c>claude plugin configure --values-stdin</c> instead (design 4.4, <c>project</c> step 2).
/// </param>
public sealed record PluginInstallRequest(string Name, string Marketplace, PluginScope Scope, IReadOnlyDictionary<string, string> Config);

/// <summary>A plugin marketplace as the agent reports it, identified by its <paramref name="Name"/>.</summary>
/// <param name="Source">The source type, such as <c>github</c>, <c>git</c> or <c>directory</c>.</param>
/// <param name="Repository">The <c>owner/repo</c> of a <c>github</c> source; <see langword="null"/> for other sources.</param>
/// <param name="Ref">The pinned branch or tag, or <see langword="null"/> when the source is not pinned.</param>
public sealed record RegisteredMarketplace(string Name, string Source, string? Repository, string? Ref)
{
    /// <summary>
    /// Whether this is exactly how the bootstrap registers <paramref name="channel"/>: its name, a <c>github</c> source
    /// for its repository spelled the same (ordinal), and no ref, as <c>strictKnownMarketplaces</c> will expect
    /// (design 4.1).
    /// </summary>
    public bool IsRegistrationOf(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return string.Equals(Name, channel.Name, StringComparison.Ordinal)
            && string.Equals(Source, "github", StringComparison.Ordinal)
            && string.Equals(Repository, channel.Repository, StringComparison.Ordinal)
            && Ref is null;
    }
}

/// <summary>
/// One installation of a plugin: its id (<c>Name@Marketplace</c>), its scope and, at local scope, the exact folder it
/// belongs to. A local plugin applies only in the folder where it was installed (observed on 2.1.300, design 4.4).
/// </summary>
/// <param name="Folder">
/// The absolute folder of a local-scope installation, as <c>projectPath</c> of <c>claude plugin list --json</c> reports
/// it; <see langword="null"/> at user scope.
/// </param>
public sealed record PluginLocation(string Name, string Marketplace, PluginScope Scope, string? Folder)
{
    /// <summary>The plugin id as Claude Code writes it: <c>name@marketplace</c>.</summary>
    public string Id => $"{Name}@{Marketplace}";
}

/// <summary>A stdio MCP server to register; secrets are never part of it.</summary>
public sealed record McpServerRegistration(string Name, PluginScope Scope, string Command, IReadOnlyList<string> Arguments);

/// <summary>An MCP server as reported by the agent, identified by its name and scope.</summary>
public sealed record InstalledMcpServer(string Name, PluginScope Scope);

/// <summary>A GitHub issue to open, for example a profile request.</summary>
public sealed record IssueDraft(string Title, string Body, IReadOnlyList<string> Labels);
