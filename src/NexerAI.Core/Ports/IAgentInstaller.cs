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
}
