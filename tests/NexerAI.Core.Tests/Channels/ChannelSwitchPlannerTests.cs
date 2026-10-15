using System.Collections.ObjectModel;
using NexerAI.Core.Channels;
using NexerAI.Core.Domain;
using NexerAI.Core.Planning;
using NexerAI.Core.State;

namespace NexerAI.Core.Tests.Channels;

public sealed class ChannelSwitchPlannerTests
{
    private const string Folder = @"C:\repos\acme";

    private static readonly ChannelSwitch ToEarly = new(Channel.Stable, Channel.Early, []);

    [Fact]
    public void Plan_disables_old_dependents_first_installs_new_core_first_then_uninstalls_old_dependents_first()
    {
        var plan = ChannelSwitchPlanner.Plan(ToEarly, PluginScope.User, null, [User("nexer-core", "nexer"), User("nexer-qa", "nexer"), User("nexer-mod", "nexer")]);

        Assert.Equal(
            [
                Disable("nexer-mod", "nexer"),
                Disable("nexer-qa", "nexer"),
                Disable("nexer-core", "nexer"),
                Install("nexer-core", "nexer-early"),
                Install("nexer-mod", "nexer-early"),
                Install("nexer-qa", "nexer-early"),
                Uninstall("nexer-mod", "nexer"),
                Uninstall("nexer-qa", "nexer"),
                Uninstall("nexer-core", "nexer"),
            ],
            plan);
    }

    [Fact]
    public void Plan_disables_the_new_id_again_when_the_person_had_disabled_the_old_one()
    {
        var pending = ToEarly with { PersonDisabled = [new PluginLocation("nexer-qa", "nexer", PluginScope.User, null)] };

        var plan = ChannelSwitchPlanner.Plan(pending, PluginScope.User, null, [User("nexer-core", "nexer"), User("nexer-qa", "nexer", enabled: false)]);

        Assert.Equal(
            [
                Disable("nexer-core", "nexer"),
                Install("nexer-core", "nexer-early"),
                Install("nexer-qa", "nexer-early"),
                Disable("nexer-qa", "nexer-early"),
                Uninstall("nexer-qa", "nexer"),
                Uninstall("nexer-core", "nexer"),
            ],
            plan);
    }

    [Fact]
    public void Plan_after_an_interruption_with_the_new_id_installed_and_the_old_one_present_only_finishes_the_move()
    {
        // The old id was disabled by the switch, not the person: it is not in PersonDisabled, so the new one stays enabled.
        var plan = ChannelSwitchPlanner.Plan(ToEarly, PluginScope.User, null, [User("nexer-core", "nexer", enabled: false), User("nexer-core", "nexer-early")]);

        Assert.Equal([Uninstall("nexer-core", "nexer")], plan);
    }

    [Fact]
    public void Plan_after_an_interruption_with_the_old_id_disabled_and_the_new_one_missing_installs_it()
    {
        var plan = ChannelSwitchPlanner.Plan(ToEarly, PluginScope.User, null, [User("nexer-core", "nexer", enabled: false)]);

        Assert.Equal([Install("nexer-core", "nexer-early"), Uninstall("nexer-core", "nexer")], plan);
    }

    [Fact]
    public void Plan_disables_a_person_disabled_new_id_that_an_interrupted_run_installed_enabled()
    {
        var pending = ToEarly with { PersonDisabled = [new PluginLocation("nexer-qa", "nexer", PluginScope.User, null)] };

        var plan = ChannelSwitchPlanner.Plan(pending, PluginScope.User, null, [User("nexer-qa", "nexer", enabled: false), User("nexer-qa", "nexer-early")]);

        Assert.Equal([Disable("nexer-qa", "nexer-early"), Uninstall("nexer-qa", "nexer")], plan);
    }

    [Fact]
    public void Plan_is_empty_once_no_old_id_is_left_and_never_touches_other_marketplaces_or_scopes()
    {
        var plan = ChannelSwitchPlanner.Plan(
            ToEarly, PluginScope.User, null, [User("nexer-core", "nexer-early"), User("tool", "acme"), Local("nexer-engram", "nexer")]);

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_at_local_scope_uses_the_person_disabled_entries_of_that_folder_only()
    {
        var pending = ToEarly with
        {
            PersonDisabled =
            [
                new PluginLocation("nexer-dev-umbraco", "nexer", PluginScope.Local, @"C:\repos\other"),
                new PluginLocation("nexer-engram", "nexer", PluginScope.Local, "c:/REPOS/acme/"),
                new PluginLocation("nexer-dev-umbraco", "nexer", PluginScope.User, null),
            ],
        };

        var plan = ChannelSwitchPlanner.Plan(
            pending, PluginScope.Local, Folder, [Local("nexer-dev-umbraco", "nexer"), Local("nexer-engram", "nexer", enabled: false), User("nexer-core", "nexer")]);

        Assert.Equal(
            [
                Disable("nexer-dev-umbraco", "nexer", PluginScope.Local),
                Install("nexer-dev-umbraco", "nexer-early", PluginScope.Local),
                Install("nexer-engram", "nexer-early", PluginScope.Local),
                Disable("nexer-engram", "nexer-early", PluginScope.Local),
                Uninstall("nexer-dev-umbraco", "nexer", PluginScope.Local),
                Uninstall("nexer-engram", "nexer", PluginScope.Local),
            ],
            plan);
    }

    [Fact]
    public void Plan_needs_a_folder_exactly_at_local_scope()
    {
        Assert.Throws<ArgumentException>(() => ChannelSwitchPlanner.Plan(ToEarly, PluginScope.Local, null, []));
        Assert.Throws<ArgumentException>(() => ChannelSwitchPlanner.Plan(ToEarly, PluginScope.User, Folder, []));
    }

    private static InstalledPlugin User(string name, string marketplace, bool enabled = true) =>
        new(name, marketplace, "1.0.0", PluginScope.User, enabled);

    private static InstalledPlugin Local(string name, string marketplace, bool enabled = true) =>
        new(name, marketplace, "1.0.0", PluginScope.Local, enabled);

    private static PlanAction.DisablePlugin Disable(string name, string marketplace, PluginScope scope = PluginScope.User) =>
        new PlanAction.DisablePlugin(name, marketplace, scope);

    private static PlanAction.UninstallPlugin Uninstall(string name, string marketplace, PluginScope scope = PluginScope.User) =>
        new PlanAction.UninstallPlugin(name, marketplace, scope);

    private static PlanAction.InstallPlugin Install(string name, string marketplace, PluginScope scope = PluginScope.User) =>
        new PlanAction.InstallPlugin(new PluginInstallRequest(name, marketplace, scope, ReadOnlyDictionary<string, string>.Empty));
}
