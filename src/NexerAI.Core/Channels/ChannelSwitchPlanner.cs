using System.Collections.ObjectModel;
using NexerAI.Core.Domain;
using NexerAI.Core.Planning;
using NexerAI.Core.State;
using NexerAI.Core.Tasks;

namespace NexerAI.Core.Channels;

/// <summary>
/// Plans step 3 of a channel switch (design 4.1) at one place: the user scope, or the local scope of one folder. The
/// plan is computed from what the agent reports now, so a run that was interrupted anywhere is finished by planning
/// again: every action is needed only while the old id is still installed there.
/// </summary>
public static class ChannelSwitchPlanner
{
    /// <summary>
    /// Returns the actions that move each <see cref="ChannelSwitch.From"/> plugin installed at <paramref name="scope"/>
    /// to <see cref="ChannelSwitch.To"/>, in four passes:
    /// <list type="number">
    /// <item>disable each old id that is enabled, dependents before <c>nexer-core</c>, so two copies are never enabled together;</item>
    /// <item>install each new id that is missing, <c>nexer-core</c> first, never one already installed (installing it again would enable it);</item>
    /// <item>disable each new id that is enabled or was just installed when the person had disabled the old one there (<see cref="ChannelSwitch.PersonDisabled"/>);</item>
    /// <item>uninstall each old id, dependents before <c>nexer-core</c>.</item>
    /// </list>
    /// Within a pass, plugins go by name. Other marketplaces, other scopes and new ids without an old one are left alone.
    /// </summary>
    /// <param name="pending">The switch in progress.</param>
    /// <param name="scope">The scope planned for.</param>
    /// <param name="folder">At <see cref="PluginScope.Local"/>, the folder <paramref name="installed"/> was read in; <see langword="null"/> at user scope.</param>
    /// <param name="installed">The plugins the agent reports in that place; entries at the other scope are ignored.</param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is missing at local scope or given at user scope.</exception>
    public static IReadOnlyList<PlanAction> Plan(ChannelSwitch pending, PluginScope scope, string? folder, IEnumerable<InstalledPlugin> installed)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(installed);
        if ((scope == PluginScope.Local) != (folder is not null))
        {
            throw new ArgumentException("A folder is required at local scope and not allowed at user scope.", nameof(folder));
        }

        var atScope = installed.Where(p => p.Scope == scope).ToList();
        var old = atScope.Where(p => p.Marketplace == pending.From.Name).DistinctBy(p => p.Name).ToList();
        var current = atScope.Where(p => p.Marketplace == pending.To.Name).DistinctBy(p => p.Name).ToDictionary(p => p.Name, StringComparer.Ordinal);
        var coreLast = old.OrderBy(p => p.Name == DesiredState.CorePlugin ? 1 : 0).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
        var coreFirst = old.OrderBy(p => p.Name == DesiredState.CorePlugin ? 0 : 1).ThenBy(p => p.Name, StringComparer.Ordinal);

        var actions = new List<PlanAction>();
        actions.AddRange(coreLast.Where(p => p.Enabled).Select(p => new PlanAction.DisablePlugin(p.Name, p.Marketplace, scope)));
        actions.AddRange(coreFirst
            .Where(p => !current.ContainsKey(p.Name))
            .Select(p => new PlanAction.InstallPlugin(new PluginInstallRequest(p.Name, pending.To.Name, scope, ReadOnlyDictionary<string, string>.Empty))));
        actions.AddRange(coreLast
            .Where(p => PersonDisabled(pending, p.Name, scope, folder) && (!current.TryGetValue(p.Name, out var now) || now.Enabled))
            .Select(p => new PlanAction.DisablePlugin(p.Name, pending.To.Name, scope)));
        actions.AddRange(coreLast.Select(p => new PlanAction.UninstallPlugin(p.Name, p.Marketplace, scope)));
        return actions;
    }

    private static bool PersonDisabled(ChannelSwitch pending, string name, PluginScope scope, string? folder) =>
        pending.PersonDisabled.Any(location =>
            location.Name == name
            && location.Marketplace == pending.From.Name
            && location.Scope == scope
            && (scope == PluginScope.User || (location.Folder is not null && WindowsPath.SameFolder(location.Folder, folder!))));
}
