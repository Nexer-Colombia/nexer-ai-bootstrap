using NexerAI.Core.Domain;

namespace NexerAI.Core.Ports;

/// <summary>
/// Installs capabilities into a coding agent; only a Claude Code adapter exists today. An instance acts in one working
/// directory, which decides the local-scope plugins it sees and changes; <see cref="ForFolder"/> gives one for another.
/// </summary>
public interface IAgentInstaller
{
    /// <summary>The same agent acting in <paramref name="folder"/>, an absolute folder: its local-scope plugins are that folder's.</summary>
    IAgentInstaller ForFolder(string folder);

    /// <summary>
    /// Returns the existing folders that hold local-scope plugins of any marketplace, each once: a local plugin belongs
    /// to the exact folder it was installed in (design 4.4).
    /// </summary>
    Task<IReadOnlyList<string>> GetPluginFoldersAsync(CancellationToken ct);

    /// <summary>Returns the plugins the agent reports as installed.</summary>
    Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the MCP servers visible from the current working directory: those at user scope and
    /// those at local scope of the current project.
    /// </summary>
    Task<IReadOnlyList<InstalledMcpServer>> GetMcpServersAsync(CancellationToken ct);

    /// <summary>Installs or updates a plugin at the requested scope.</summary>
    Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct);

    /// <summary>Disables a plugin at <paramref name="scope"/> without uninstalling it; one already disabled there counts as done.</summary>
    Task DisablePluginAsync(string name, string marketplace, PluginScope scope, CancellationToken ct);

    /// <summary>Uninstalls a plugin from <paramref name="scope"/>; one not installed there counts as done.</summary>
    Task UninstallPluginAsync(string name, string marketplace, PluginScope scope, CancellationToken ct);

    /// <summary>Registers an MCP server at the requested scope.</summary>
    Task AddMcpServerAsync(McpServerRegistration registration, CancellationToken ct);

    /// <summary>Returns the plugin marketplaces the agent has registered.</summary>
    Task<IReadOnlyList<RegisteredMarketplace>> GetMarketplacesAsync(CancellationToken ct);

    /// <summary>
    /// Registers the marketplace of <paramref name="channel"/> for the current user, from <see cref="Channel.Repository"/>.
    /// Callers check <see cref="GetMarketplacesAsync"/> first and never add a registered marketplace again
    /// (see <see cref="Channels.ChannelRegistration"/>).
    /// </summary>
    Task AddMarketplaceAsync(Channel channel, CancellationToken ct);

    /// <summary>
    /// Makes the agent keep the marketplace of <paramref name="channel"/> and its plugins up to date by itself; does
    /// nothing when it already does.
    /// </summary>
    Task EnableMarketplaceAutoUpdateAsync(Channel channel, CancellationToken ct);

    /// <summary>
    /// Removes the marketplace <paramref name="name"/> from the user's settings, which also uninstalls its plugins in
    /// every scope; one that is not registered counts as done.
    /// </summary>
    Task RemoveMarketplaceAsync(string name, CancellationToken ct);
}
