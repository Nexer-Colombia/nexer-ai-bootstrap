using NexerAI.Core.Domain;
using NexerAI.Core.Planning;
using NexerAI.Core.Ports;
using NexerAI.Core.State;
using NexerAI.Core.Tasks;

namespace NexerAI.Core.Channels;

/// <summary>
/// Moves a person's plugins from one channel marketplace to the other (design 4.1, "Switching channel", tested on Claude
/// Code 2.1.301). Plugin ids change with the marketplace, so each plugin's data directory and saved options start empty
/// on the new channel; the caller tells the person so, and to restart Claude Code or run <c>/reload-plugins</c>.
/// </summary>
/// <remarks>
/// Each step reads the agent's state first and acts only on what is left (<see cref="ChannelSwitchPlanner"/>), and the
/// switch is recorded in the state file before the first change, so any later run finishes an interrupted switch
/// instead of reverting it, even when it is asked for the other channel (it moves back afterwards). The one exception
/// is a refused registration of the new marketplace (the same name registered from another source) when no plugin was
/// touched yet: the record is cleared then, so the person is not stuck, can stay on the channel they have, and a rerun
/// toward the same target starts again once the marketplace is fixed. The channel's auto-update is checked on every
/// run, because installs and <c>marketplace add</c> can drop it.
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
    /// <exception cref="AgentStateException">
    /// A step failed; the switch stays recorded and the next run finishes it, except for a refused registration before
    /// any plugin was touched, which leaves nothing recorded.
    /// </exception>
    public static async Task<bool> SwitchAsync(IAgentInstaller installer, IStateStore stateStore, Channel target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(stateStore);
        ArgumentNullException.ThrowIfNull(target);

        var pendingBefore = (await stateStore.ReadAsync(ct).ConfigureAwait(false)).Switch;
        bool resumed;
        try
        {
            resumed = await ResumeAsync(installer, stateStore, ct).ConfigureAwait(false);
        }
        catch (AgentStateException) when (pendingBefore is not null && pendingBefore.From == target && pendingBefore.To != target)
        {
            if ((await stateStore.ReadAsync(ct).ConfigureAwait(false)).Switch is not null)
            {
                throw; // Kept for a rerun: something moved, or the failure was not the refusal.
            }

            // The pending switch was refused before anything moved and is dropped; the person only asked for the
            // channel they are on, so there is nothing to report: keep its registration and auto-update right.
            await ChannelRegistration.EnsureAsync(installer, target, ct).ConfigureAwait(false);
            return false;
        }

        var state = await stateStore.ReadAsync(ct).ConfigureAwait(false);
        if (state.Channel is not { } from)
        {
            return resumed;
        }

        if (from == target)
        {
            if (resumed)
            {
                return true; // The finished switch just checked it.
            }

            await ChannelRegistration.EnsureAsync(installer, target, ct).ConfigureAwait(false);
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
    /// <exception cref="AgentStateException">A step failed; the switch stays recorded, except for a refused registration before any plugin was touched.</exception>
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
        try
        {
            await ChannelRegistration.EnsureAsync(installer, pending.To, ct).ConfigureAwait(false);
        }
        catch (AgentStateException)
        {
            // Nothing changed, and the pending switch would make every later run, even one that stays on the current
            // channel, fail with the same refusal: drop it. A rerun toward the same target records it again.
            if (await RegistrationRefusedBeforeAnyMoveAsync(installer, pending, ct).ConfigureAwait(false))
            {
                await stateStore.UpdateAsync(current => current with { Switch = null }, ct).ConfigureAwait(false);
            }

            throw;
        }

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

        // The installs and the removal can drop the auto-update of the new marketplace (design 4.1): check it again.
        await ChannelRegistration.EnsureAsync(installer, pending.To, ct).ConfigureAwait(false);

        // Step 5.
        await stateStore.UpdateAsync(current => current with { Channel = pending.To, Switch = null }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the new marketplace's name is registered from another source (the refusal of <see cref="ChannelRegistration"/>)
    /// and every <see cref="ChannelSwitch.From"/> plugin is still as the person had it: enabled unless recorded as
    /// disabled by the person, so no step of the move ran.
    /// </summary>
    private static async Task<bool> RegistrationRefusedBeforeAnyMoveAsync(IAgentInstaller installer, ChannelSwitch pending, CancellationToken ct)
    {
        var marketplaces = await installer.GetMarketplacesAsync(ct).ConfigureAwait(false);
        var registered = marketplaces.FirstOrDefault(m => string.Equals(m.Name, pending.To.Name, StringComparison.Ordinal));
        if (registered is null || registered.IsRegistrationOf(pending.To))
        {
            return false;
        }

        var places = new List<(IAgentInstaller Agent, PluginScope Scope, string? Folder)> { (installer, PluginScope.User, null) };
        foreach (var folder in await installer.GetPluginFoldersAsync(ct).ConfigureAwait(false))
        {
            places.Add((installer.ForFolder(folder), PluginScope.Local, folder));
        }

        foreach (var (agent, scope, folder) in places)
        {
            foreach (var plugin in await agent.GetInstalledPluginsAsync(ct).ConfigureAwait(false))
            {
                if (plugin.Scope == scope && plugin.Marketplace == pending.From.Name
                    && plugin.Enabled == pending.PersonDisabled.Any(l => l.Name == plugin.Name && l.Scope == scope && (scope == PluginScope.User || WindowsPath.SameFolder(l.Folder!, folder!))))
                {
                    return false;
                }
            }
        }

        return true;
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
