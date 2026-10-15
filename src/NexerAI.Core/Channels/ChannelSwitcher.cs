using NexerAI.Core.Domain;
using NexerAI.Core.Planning;
using NexerAI.Core.Ports;
using NexerAI.Core.State;

namespace NexerAI.Core.Channels;

/// <summary>
/// Moves a person's plugins from one channel marketplace to the other (design 4.1, "Switching channel", tested on Claude
/// Code 2.1.301). Plugin ids change with the marketplace, so each plugin's data directory and saved options start empty
/// on the new channel; the caller tells the person so, and to restart Claude Code or run <c>/reload-plugins</c>.
/// </summary>
/// <remarks>
/// Each step reads the agent's state first and acts only on what is left (<see cref="ChannelSwitchPlanner"/>), and the
/// switch is recorded in the state file before the first change, so any later run finishes an interrupted switch
/// instead of reverting it.
/// </remarks>
public static class ChannelSwitcher
{
    /// <summary>
    /// Finishes a pending switch (<see cref="ResumeAsync"/>), then switches to <paramref name="target"/> when the state
    /// file names another channel. Before the first install chose a channel there is nothing to move.
    /// </summary>
    /// <param name="installer">
    /// The agent, for user-scope plugins; <see cref="IAgentInstaller.ForFolder"/> gives one per project folder. Bind it to
    /// a folder without project settings, such as the user profile: a plugin's <c>enabled</c> is read in that folder,
    /// where a project's local override would hide the user-scope state.
    /// </param>
    /// <param name="stateStore">The state file.</param>
    /// <param name="target">The channel the person wants.</param>
    /// <returns>Whether any plugin or marketplace may have changed, so the person must restart or reload.</returns>
    /// <exception cref="AgentStateException">A step failed; the switch stays recorded and the next run finishes it.</exception>
    public static async Task<bool> SwitchAsync(IAgentInstaller installer, IStateStore stateStore, Channel target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(target);

        var resumed = await ResumeAsync(installer, stateStore, ct).ConfigureAwait(false);
        var state = await stateStore.ReadAsync(ct).ConfigureAwait(false);
        if (state.Channel is not { } from || from == target)
        {
            return resumed;
        }

        // Step 1: record the target and what the person had disabled, before anything changes.
        var personDisabled = await DisabledPluginsAsync(installer, from, ct).ConfigureAwait(false);
        var recorded = await stateStore.UpdateAsync(
            current => current.Switch is null ? current with { Switch = new ChannelSwitch(from, target, personDisabled) } : current, ct)
            .ConfigureAwait(false);
        await FinishAsync(installer, stateStore, recorded.Switch!, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Finishes the switch the state file records, if any: what <c>nexer-ai install</c> runs first, so a rerun never
    /// reverts a switch.
    /// </summary>
    /// <returns>Whether a switch was pending and is now finished.</returns>
    /// <exception cref="AgentStateException">A step failed; the switch stays recorded.</exception>
    public static async Task<bool> ResumeAsync(IAgentInstaller installer, IStateStore stateStore, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(stateStore);

        var state = await stateStore.ReadAsync(ct).ConfigureAwait(false);
        if (state.Switch is not { } pending)
        {
            return false;
        }

        await FinishAsync(installer, stateStore, pending, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>Steps 2 to 5: register the new marketplace, move the plugins, remove the old marketplace, clear the switch.</summary>
    private static async Task FinishAsync(IAgentInstaller installer, IStateStore stateStore, ChannelSwitch pending, CancellationToken ct)
    {
        await ChannelRegistration.EnsureAsync(installer, pending.To, ct).ConfigureAwait(false);

        // Step 3: user scope first, then the local scope of each folder, each read from its own folder.
        await MoveAsync(installer, pending, PluginScope.User, null, ct).ConfigureAwait(false);
        foreach (var folder in await installer.GetPluginFoldersAsync(ct).ConfigureAwait(false))
        {
            await MoveAsync(installer.ForFolder(folder), pending, PluginScope.Local, folder, ct).ConfigureAwait(false);
        }

        // Step 4: removing it also uninstalls what is left of it in every scope (design 4.1).
        var marketplaces = await installer.GetMarketplacesAsync(ct).ConfigureAwait(false);
        if (marketplaces.Any(m => string.Equals(m.Name, pending.From.Name, StringComparison.Ordinal)))
        {
            await installer.RemoveMarketplaceAsync(pending.From.Name, ct).ConfigureAwait(false);
        }

        // Step 5.
        await stateStore.UpdateAsync(current => current with { Channel = pending.To, Switch = null }, ct).ConfigureAwait(false);
    }

    private static async Task MoveAsync(IAgentInstaller agent, ChannelSwitch pending, PluginScope scope, string? folder, CancellationToken ct)
    {
        var installed = await agent.GetInstalledPluginsAsync(ct).ConfigureAwait(false);
        foreach (var action in ChannelSwitchPlanner.Plan(pending, scope, folder, installed))
        {
            await (action switch
            {
                PlanAction.DisablePlugin disable => agent.DisablePluginAsync(disable.Name, disable.Marketplace, disable.Scope, ct),
                PlanAction.InstallPlugin install => agent.InstallPluginAsync(install.Request, ct),
                PlanAction.UninstallPlugin uninstall => agent.UninstallPluginAsync(uninstall.Name, uninstall.Marketplace, uninstall.Scope, ct),
                _ => throw new InvalidOperationException($"A channel switch does not plan {action.GetType().Name}."),
            }).ConfigureAwait(false);
        }
    }

    /// <summary>The <paramref name="from"/> plugins disabled now: at user scope, then at the local scope of each folder, read in that folder.</summary>
    private static async Task<IReadOnlyList<PluginLocation>> DisabledPluginsAsync(IAgentInstaller installer, Channel from, CancellationToken ct)
    {
        var disabled = new List<PluginLocation>();
        foreach (var plugin in await installer.GetInstalledPluginsAsync(ct).ConfigureAwait(false))
        {
            if (plugin is { Scope: PluginScope.User, Enabled: false } && plugin.Marketplace == from.Name)
            {
                disabled.Add(new PluginLocation(plugin.Name, plugin.Marketplace, PluginScope.User, null));
            }
        }

        foreach (var folder in await installer.GetPluginFoldersAsync(ct).ConfigureAwait(false))
        {
            foreach (var plugin in await installer.ForFolder(folder).GetInstalledPluginsAsync(ct).ConfigureAwait(false))
            {
                if (plugin is { Scope: PluginScope.Local, Enabled: false } && plugin.Marketplace == from.Name)
                {
                    disabled.Add(new PluginLocation(plugin.Name, plugin.Marketplace, PluginScope.Local, folder));
                }
            }
        }

        return disabled.AsReadOnly();
    }
}
