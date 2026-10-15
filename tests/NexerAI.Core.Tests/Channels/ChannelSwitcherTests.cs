using NexerAI.Core.Channels;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;
using NexerAI.Core.State;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.Channels;

/// <summary>
/// The switch against a stateful fake agent: user-scope plugins shared by every folder, local plugins per folder, and
/// "already done" commands succeeding as the adapter maps them. Interruptions are injected with
/// <see cref="FakeAgentInstaller.FailOnce"/> and finished by running again.
/// </summary>
public sealed class ChannelSwitcherTests
{
    private const string FolderA = @"C:\repos\a";

    private const string FolderB = @"C:\repos\b";

    private static readonly RegisteredMarketplace Stable = new("nexer", "github", "Nexer-Colombia/nexer-ai-marketplace", null);

    private readonly FakeAgentInstaller agent = new()
    {
        Marketplaces = [Stable],
        Installed = [User("nexer-core", "nexer"), User("nexer-qa", "nexer")],
    };

    private readonly FakeStateStore store = new() { State = InstallState.Empty with { Channel = Channel.Stable, Role = Role.Qa } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SwitchAsync_registers_the_new_marketplace_moves_every_plugin_removes_the_old_marketplace_and_records_the_channel()
    {
        agent.WithFolder(FolderA, Local("nexer-dev-umbraco", "nexer"));

        Assert.True(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));

        Assert.Equal(
            [
                "marketplace add Nexer-Colombia/nexer-ai-marketplace-early",
                "marketplace autoUpdate nexer-early",
                "disable nexer-qa@nexer --scope user",
                "disable nexer-core@nexer --scope user",
                "install nexer-core@nexer-early --scope user",
                "install nexer-qa@nexer-early --scope user",
                "uninstall nexer-qa@nexer --scope user",
                "uninstall nexer-core@nexer --scope user",
                $"disable nexer-dev-umbraco@nexer --scope local in {FolderA}",
                $"install nexer-dev-umbraco@nexer-early --scope local in {FolderA}",
                $"uninstall nexer-dev-umbraco@nexer --scope local in {FolderA}",
                "marketplace remove nexer",
                "marketplace autoUpdate nexer-early",
            ],
            agent.Calls);
        Assert.Equal([User("nexer-core", "nexer-early"), User("nexer-qa", "nexer-early")], agent.Installed);
        Assert.Equal([Local("nexer-dev-umbraco", "nexer-early")], ((FakeAgentInstaller)agent.ForFolder(FolderA)).Installed);
        Assert.Equal(["nexer-early"], agent.Marketplaces.Select(m => m.Name));
        Assert.Same(Channel.Early, store.State.Channel);
        Assert.Null(store.State.Switch);
        Assert.Equal(Role.Qa, store.State.Role);
    }

    [Fact]
    public async Task SwitchAsync_records_the_target_and_the_disabled_plugins_before_the_first_change()
    {
        agent.Installed[1] = User("nexer-qa", "nexer", enabled: false);
        agent.WithFolder(FolderA, Local("nexer-dev-umbraco", "nexer", enabled: false));
        agent.FailOnce = "marketplace add";

        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));

        var pending = store.State.Switch!;
        Assert.Equal((Channel.Stable, Channel.Early), (pending.From, pending.To));
        Assert.Equal(
            [new PluginLocation("nexer-qa", "nexer", PluginScope.User, null), new PluginLocation("nexer-dev-umbraco", "nexer", PluginScope.Local, FolderA)],
            pending.PersonDisabled);
        Assert.Same(Channel.Stable, store.State.Channel);
        Assert.Empty(agent.Calls);
    }

    [Fact]
    public async Task A_person_disabled_plugin_stays_disabled_in_the_place_where_it_was_disabled_only()
    {
        agent.Installed[1] = User("nexer-qa", "nexer", enabled: false);
        agent.WithFolder(FolderA, Local("nexer-dev-umbraco", "nexer", enabled: false));
        agent.WithFolder(FolderB, Local("nexer-dev-umbraco", "nexer"));

        await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct);

        Assert.Equal([User("nexer-core", "nexer-early"), User("nexer-qa", "nexer-early", enabled: false)], agent.Installed);
        Assert.Equal([Local("nexer-dev-umbraco", "nexer-early", enabled: false)], ((FakeAgentInstaller)agent.ForFolder(FolderA)).Installed);
        Assert.Equal([Local("nexer-dev-umbraco", "nexer-early")], ((FakeAgentInstaller)agent.ForFolder(FolderB)).Installed);
        Assert.DoesNotContain("disable nexer-qa@nexer --scope user", agent.Calls);
    }

    [Theory]
    [InlineData("uninstall nexer-core@nexer --scope user")]
    [InlineData("install nexer-qa@nexer-early")]
    [InlineData("disable nexer-qa@nexer-early")]
    [InlineData("install nexer-dev-umbraco@nexer-early")]
    [InlineData("marketplace remove nexer")]
    [InlineData("marketplace autoUpdate")]
    public async Task A_run_interrupted_anywhere_is_finished_by_any_later_run_without_installing_twice(string failure)
    {
        agent.Installed[1] = User("nexer-qa", "nexer", enabled: false);
        agent.WithFolder(FolderA, Local("nexer-dev-umbraco", "nexer"));
        agent.FailOnce = failure;
        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));
        Assert.NotNull(store.State.Switch);

        // A plain install finishes the switch instead of reverting it.
        Assert.True(await ChannelSwitcher.ResumeAsync(agent, store, Ct));

        Assert.Equal([User("nexer-core", "nexer-early"), User("nexer-qa", "nexer-early", enabled: false)], agent.Installed);
        Assert.Equal([Local("nexer-dev-umbraco", "nexer-early")], ((FakeAgentInstaller)agent.ForFolder(FolderA)).Installed);
        Assert.Equal(["nexer-early"], agent.Marketplaces.Select(m => m.Name));
        Assert.Equal((Channel.Early, (ChannelSwitch?)null), (store.State.Channel, store.State.Switch));
        Assert.All(agent.Calls.Where(c => c.StartsWith("install", StringComparison.Ordinal)).GroupBy(c => c), group => Assert.Single(group));
        Assert.Single(agent.Calls, c => c == "marketplace add Nexer-Colombia/nexer-ai-marketplace-early");
    }

    [Fact]
    public async Task A_rerun_with_only_the_old_marketplace_left_removes_it_and_touches_no_plugin()
    {
        agent.FailOnce = "marketplace remove";
        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));
        agent.Calls.Clear();

        await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct);

        Assert.Equal(["marketplace autoUpdate nexer-early", "marketplace remove nexer", "marketplace autoUpdate nexer-early"], agent.Calls);
        Assert.Null(store.State.Switch);
    }

    [Fact]
    public async Task A_rerun_after_the_switch_completed_only_checks_the_auto_update_of_the_channel()
    {
        await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct);
        agent.Calls.Clear();
        var updates = store.UpdateCount;

        // Installs and "marketplace add" can drop the setting, so every run checks it again (design 4.1).
        Assert.False(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));
        Assert.False(await ChannelSwitcher.ResumeAsync(agent, store, Ct));

        Assert.Equal(["marketplace autoUpdate nexer-early"], agent.Calls);
        Assert.Equal(updates, store.UpdateCount);
    }

    [Fact]
    public async Task The_auto_update_is_checked_last_because_a_plugin_install_can_drop_it()
    {
        await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct);

        Assert.Equal(
            ["marketplace autoUpdate nexer-early", "marketplace remove nexer", "marketplace autoUpdate nexer-early"],
            agent.Calls.Where(c => c.StartsWith("marketplace autoUpdate", StringComparison.Ordinal) || c == "marketplace remove nexer").ToList());
        Assert.Equal("marketplace autoUpdate nexer-early", agent.Calls[^1]);
    }

    [Fact]
    public async Task A_refused_registration_that_changed_nothing_leaves_no_pending_switch_so_the_person_can_stay_or_retry()
    {
        var foreign = new RegisteredMarketplace("nexer-early", "github", "someone/else", null);
        agent.Marketplaces.Add(foreign);

        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));

        Assert.Null(store.State.Switch);
        Assert.Same(Channel.Stable, store.State.Channel);
        Assert.Empty(agent.Calls);

        // Staying on the original channel is not blocked by the refusal.
        Assert.False(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Stable, Ct));
        Assert.False(await ChannelSwitcher.ResumeAsync(agent, store, Ct));

        // Once the person removed the foreign marketplace, the same request goes through.
        agent.Marketplaces.Remove(foreign);
        Assert.True(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));
        Assert.Equal((Channel.Early, (ChannelSwitch?)null), (store.State.Channel, store.State.Switch));
        Assert.Equal([User("nexer-core", "nexer-early"), User("nexer-qa", "nexer-early")], agent.Installed);
    }

    [Fact]
    public async Task A_refusal_after_plugins_were_moved_keeps_the_pending_switch_so_a_rerun_finishes_it()
    {
        agent.FailOnce = "install nexer-qa@nexer-early";
        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));
        var early = agent.Marketplaces.Single(m => m.Name == "nexer-early");
        agent.Marketplaces[agent.Marketplaces.IndexOf(early)] = early with { Repository = "someone/else" };

        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.ResumeAsync(agent, store, Ct));

        Assert.NotNull(store.State.Switch);
    }

    [Fact]
    public async Task SwitchAsync_back_to_the_old_channel_after_an_interruption_in_the_middle_of_the_move_ends_on_the_old_channel()
    {
        agent.Installed[1] = User("nexer-qa", "nexer", enabled: false);
        agent.FailOnce = "install nexer-qa@nexer-early";
        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));

        Assert.True(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Stable, Ct));

        Assert.Equal([User("nexer-core", "nexer"), User("nexer-qa", "nexer", enabled: false)], agent.Installed);
        Assert.Equal(["nexer"], agent.Marketplaces.Select(m => m.Name));
        Assert.Equal((Channel.Stable, (ChannelSwitch?)null), (store.State.Channel, store.State.Switch));
    }

    [Fact]
    public async Task SwitchAsync_back_to_the_old_channel_while_a_switch_is_pending_finishes_it_first()
    {
        agent.FailOnce = "uninstall";
        await Assert.ThrowsAsync<AgentStateException>(() => ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));

        Assert.True(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Stable, Ct));

        Assert.Equal([User("nexer-core", "nexer"), User("nexer-qa", "nexer")], agent.Installed);
        Assert.Equal(["nexer"], agent.Marketplaces.Select(m => m.Name));
        Assert.Equal((Channel.Stable, (ChannelSwitch?)null), (store.State.Channel, store.State.Switch));
    }

    [Fact]
    public async Task SwitchAsync_does_nothing_before_the_first_install_chose_a_channel()
    {
        store.State = InstallState.Empty;

        Assert.False(await ChannelSwitcher.SwitchAsync(agent, store, Channel.Early, Ct));

        Assert.Empty(agent.Calls);
        Assert.Null(store.State.Switch);
    }

    private static InstalledPlugin User(string name, string marketplace, bool enabled = true) =>
        new(name, marketplace, "1.0.0", PluginScope.User, enabled);

    private static InstalledPlugin Local(string name, string marketplace, bool enabled = true) =>
        new(name, marketplace, "1.0.0", PluginScope.Local, enabled);
}
