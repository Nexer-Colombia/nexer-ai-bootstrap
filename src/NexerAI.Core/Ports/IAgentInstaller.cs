using NexerAI.Core.Domain;

namespace NexerAI.Core.Ports;

/// <summary>Installs capabilities into a coding agent; only a Claude Code adapter exists today.</summary>
public interface IAgentInstaller
{
    /// <summary>Returns the plugins the agent reports as installed.</summary>
    Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the MCP servers visible from the current working directory: those at user scope and
    /// those at local scope of the current project.
    /// </summary>
    Task<IReadOnlyList<InstalledMcpServer>> GetMcpServersAsync(CancellationToken ct);

    /// <summary>Installs or updates a plugin at the requested scope.</summary>
    Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct);

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
}
