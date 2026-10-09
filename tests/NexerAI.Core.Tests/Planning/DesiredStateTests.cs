using NexerAI.Core.Domain;
using NexerAI.Core.Planning;

namespace NexerAI.Core.Tests.Planning;

public sealed class DesiredStateTests
{
    private const string Exe = @"C:\Users\ana\AppData\Local\nexer-ai\bin\nexer-ai.exe";

    private static readonly TrackerReference Jira = new("jira", new Uri("https://acme.atlassian.net"), "WEB");

    private static readonly TrackerReference AzureDevOps = new("azure-devops", new Uri("https://dev.azure.com/acme/My%20Project"), null);

    [Theory]
    [InlineData(Role.Developer, new[] { "nexer-core" })]
    [InlineData(Role.Qa, new[] { "nexer-core", "nexer-qa" })]
    [InlineData(Role.ProductOwner, new[] { "nexer-core", "nexer-po" })]
    public void ForMachine_installs_core_and_the_role_plugin_at_user_scope(Role role, string[] expected)
    {
        var state = DesiredState.ForMachine(role, Channel.Stable, includeMod: false);

        Assert.Equal(expected, state.Plugins.Select(p => p.Name));
        Assert.All(state.Plugins, p => Assert.Equal(("nexer", PluginScope.User), (p.Marketplace, p.Scope)));
        Assert.All(state.Plugins, p => Assert.Empty(p.Config));
        Assert.Empty(state.McpServers);
    }

    [Fact]
    public void ForMachine_adds_the_mod_when_opted_in_and_uses_the_channel_marketplace()
    {
        var state = DesiredState.ForMachine(Role.Qa, Channel.Early, includeMod: true);

        Assert.Equal(["nexer-core", "nexer-qa", "nexer-mod"], state.Plugins.Select(p => p.Name));
        Assert.All(state.Plugins, p => Assert.Equal(("nexer-early", PluginScope.User), (p.Marketplace, p.Scope)));
    }

    [Fact]
    public void ForMachine_rejects_a_null_channel_and_an_undefined_role()
    {
        Assert.Throws<ArgumentNullException>(() => DesiredState.ForMachine(Role.Developer, null!, includeMod: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => DesiredState.ForMachine((Role)42, Channel.Stable, includeMod: false));
    }

    [Fact]
    public void ForProject_installs_stack_plugins_and_engram_at_local_scope_from_the_channel_marketplace()
    {
        var state = DesiredState.ForProject(Profile(["nexer-dev-umbraco", "nexer-dev-angular"], [], engram: true), Channel.Early, Exe);

        Assert.Equal(["nexer-dev-angular", "nexer-dev-umbraco", "nexer-engram"], state.Plugins.Select(p => p.Name));
        Assert.All(state.Plugins, p => Assert.Equal(("nexer-early", PluginScope.Local), (p.Marketplace, p.Scope)));
        Assert.All(state.Plugins, p => Assert.Empty(p.Config));
        Assert.Empty(state.McpServers);
    }

    [Fact]
    public void ForProject_leaves_out_engram_when_the_profile_does_not_use_it()
    {
        var state = DesiredState.ForProject(Profile(["nexer-dev-umbraco"], [], engram: false), Channel.Stable, Exe);

        Assert.Equal(["nexer-dev-umbraco"], state.Plugins.Select(p => p.Name));
    }

    [Fact]
    public void ForProject_registers_a_launcher_per_tracker_at_local_scope_with_the_url_as_written()
    {
        var state = DesiredState.ForProject(Profile([], [Jira, AzureDevOps], engram: false), Channel.Stable, Exe);

        Assert.Equal(["nexer-azure-devops", "nexer-jira"], state.McpServers.Select(s => s.Name));
        Assert.All(state.McpServers, s => Assert.Equal((PluginScope.Local, Exe), (s.Scope, s.Command)));
        Assert.Equal(
            ["mcp-launch", "--type", "azure-devops", "--url", "https://dev.azure.com/acme/My%20Project"],
            state.McpServers[0].Arguments);
        Assert.Equal(["mcp-launch", "--type", "jira", "--url", "https://acme.atlassian.net"], state.McpServers[1].Arguments);
    }

    [Fact]
    public void ForProject_lists_each_plugin_and_tracker_once()
    {
        var sameJira = new TrackerReference("jira", new Uri("https://acme.atlassian.net"), "API");

        var state = DesiredState.ForProject(
            Profile(["nexer-dev-umbraco", "nexer-engram", "nexer-dev-umbraco"], [Jira, sameJira], engram: true), Channel.Stable, Exe);

        Assert.Equal(["nexer-dev-umbraco", "nexer-engram"], state.Plugins.Select(p => p.Name));
        Assert.Equal("nexer-jira", Assert.Single(state.McpServers).Name);
    }

    [Fact]
    public void ForProject_rejects_two_trackers_of_one_type_with_different_urls()
    {
        var otherJira = new TrackerReference("jira", new Uri("https://other.atlassian.net"), null);

        var error = Assert.Throws<ArgumentException>(
            () => DesiredState.ForProject(Profile([], [Jira, otherJira], engram: false), Channel.Stable, Exe));

        Assert.Contains("jira", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForProject_returns_nothing_for_an_empty_profile()
    {
        var state = DesiredState.ForProject(Profile([], [], engram: false), Channel.Stable, Exe);

        Assert.Empty(state.Plugins);
        Assert.Empty(state.McpServers);
    }

    [Fact]
    public void ForProject_rejects_null_arguments_a_relative_executable_and_a_relative_tracker_url()
    {
        var profile = Profile([], [], engram: false);
        var relativeUrl = Profile([], [new TrackerReference("jira", new Uri("acme", UriKind.Relative), null)], engram: false);

        Assert.Throws<ArgumentNullException>(() => DesiredState.ForProject(null!, Channel.Stable, Exe));
        Assert.Throws<ArgumentNullException>(() => DesiredState.ForProject(profile, null!, Exe));
        Assert.Throws<ArgumentNullException>(() => DesiredState.ForProject(profile, Channel.Stable, null!));
        Assert.Throws<ArgumentException>(() => DesiredState.ForProject(profile, Channel.Stable, "nexer-ai.exe"));
        Assert.Throws<ArgumentException>(() => DesiredState.ForProject(relativeUrl, Channel.Stable, Exe));
    }

    private static ProjectProfile Profile(string[] plugins, TrackerReference[] trackers, bool engram) =>
        new("acme-website", ["https://github.com/acme/website"], "@lead", plugins, trackers, engram);
}
