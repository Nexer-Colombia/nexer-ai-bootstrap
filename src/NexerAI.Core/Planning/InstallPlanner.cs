using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Planning;

/// <summary>
/// Diffs a <see cref="DesiredState"/> against what the agent reports, so running a command again fixes
/// drift and does nothing when the machine already matches. Applying the plan is up to the caller.
/// </summary>
public static class InstallPlanner
{
    /// <summary>Reads the installed plugins and MCP servers from <paramref name="installer"/> and plans with <see cref="Plan"/>.</summary>
    public static async Task<IReadOnlyList<PlanAction>> PlanAsync(DesiredState desired, IAgentInstaller installer, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(installer);

        var plugins = await installer.GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        var servers = await installer.GetMcpServersAsync(ct).ConfigureAwait(false);
        return Plan(desired, plugins, servers);
    }

    /// <summary>
    /// Returns the actions that bring the actual state to <paramref name="desired"/>, each desired entry once.
    /// A plugin is present when one with the same name, marketplace and scope is installed, whatever its
    /// version (updates come from marketplace auto-update); an MCP server when one with the same name and
    /// scope exists. Names compare ordinally. Order: user-scope plugins (<c>nexer-core</c> first, then by
    /// name), local-scope plugins by name, then MCP servers by name.
    /// </summary>
    /// <remarks>Nothing is removed: plugins and servers the desired state does not list are left alone.</remarks>
    public static IReadOnlyList<PlanAction> Plan(
        DesiredState desired, IEnumerable<InstalledPlugin> installedPlugins, IEnumerable<InstalledMcpServer> installedMcpServers)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(installedPlugins);
        ArgumentNullException.ThrowIfNull(installedMcpServers);

        var plugins = installedPlugins.Select(p => (p.Name, p.Marketplace, p.Scope)).ToHashSet();
        var servers = installedMcpServers.Select(s => (s.Name, s.Scope)).ToHashSet();

        var pluginActions = desired.Plugins
            .DistinctBy(r => (r.Name, r.Marketplace, r.Scope))
            .Where(r => !plugins.Contains((r.Name, r.Marketplace, r.Scope)))
            .OrderBy(r => r.Scope == PluginScope.User ? 0 : 1)
            .ThenBy(r => r.Scope == PluginScope.User && r.Name == DesiredState.CorePlugin ? 0 : 1)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Marketplace, StringComparer.Ordinal)
            .Select(r => (PlanAction)new PlanAction.InstallPlugin(r));
        var serverActions = desired.McpServers
            .DistinctBy(s => (s.Name, s.Scope))
            .Where(s => !servers.Contains((s.Name, s.Scope)))
            .OrderBy(s => s.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Scope)
            .Select(s => new PlanAction.AddMcpServer(s));

        return [.. pluginActions, .. serverActions];
    }
}
