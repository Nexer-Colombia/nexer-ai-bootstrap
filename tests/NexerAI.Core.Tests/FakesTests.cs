using NexerAI.Core.Domain;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests;

public sealed class FakesTests
{
    private static readonly Uri AzureDevOps = new("https://dev.azure.com/acme");

    [Fact]
    public async Task ProfileSource_returns_profiles_of_the_requested_channel_only()
    {
        var profile = new ProjectProfile(
            "acme-website", ["github.com/acme/website"], "@lead", ["nexer-dev-umbraco"],
            [new TrackerReference("azure-devops", AzureDevOps, "web")], Engram: true);
        var source = new FakeProfileSource();
        source.Profiles[Channel.Early] = [profile];

        Assert.Equal([profile], await source.GetProfilesAsync(Channel.Early, CancellationToken.None));
        Assert.Empty(await source.GetProfilesAsync(Channel.Stable, CancellationToken.None));
        Assert.Equal([Channel.Early, Channel.Stable], source.RequestedChannels);
    }

    [Fact]
    public void StackDetector_returns_canned_signals_and_records_paths()
    {
        var detector = new FakeStackDetector { Signals = [new StackSignal("umbraco", "nexer-dev-umbraco")] };

        var signals = detector.Detect(@"C:\src\website");

        Assert.Equal("nexer-dev-umbraco", Assert.Single(signals).SuggestedPlugin);
        Assert.Equal([@"C:\src\website"], detector.DetectedPaths);
    }

    [Fact]
    public void Prompter_answers_in_order_and_fails_when_an_answer_is_missing()
    {
        var prompter = new FakePrompter();
        prompter.Answers.Enqueue("qa");
        prompter.Confirmations.Enqueue(true);

        Assert.Equal("qa", prompter.Choose("Role?", ["developer", "qa", "po"]));
        Assert.True(prompter.Confirm("Install the mod?"));
        Assert.Equal(["Role?", "Install the mod?"], prompter.Questions);
        Assert.Throws<InvalidOperationException>(() => prompter.ReadSecret("Token"));
    }

    [Fact]
    public void Prompter_rejects_a_canned_choice_that_was_not_offered()
    {
        var prompter = new FakePrompter();
        prompter.Answers.Enqueue("admin");

        Assert.Throws<InvalidOperationException>(() => prompter.Choose("Role?", ["developer", "qa"]));
    }

    [Fact]
    public async Task AgentInstaller_reports_configured_plugins_and_records_changes()
    {
        var installer = new FakeAgentInstaller
        {
            Installed = [new InstalledPlugin("nexer-core", "nexer", "1.2.0", PluginScope.User)],
            InstalledMcpServers = [new InstalledMcpServer("nexer-testrail", PluginScope.Local)],
        };
        var install = new PluginInstallRequest("nexer-qa", "nexer", PluginScope.User, new Dictionary<string, string>());
        var mcp = new McpServerRegistration("nexer-jira", PluginScope.Local, "nexer-ai", ["mcp-launch", "jira"]);

        var installed = await installer.GetInstalledPluginsAsync(CancellationToken.None);
        var servers = await installer.GetMcpServersAsync(CancellationToken.None);
        await installer.InstallPluginAsync(install, CancellationToken.None);
        await installer.AddMcpServerAsync(mcp, CancellationToken.None);

        Assert.Equal("nexer", Assert.Single(installed).Marketplace);
        Assert.Equal("nexer-testrail", Assert.Single(servers).Name);
        Assert.Equal([install], installer.InstallRequests);
        Assert.Equal([mcp], installer.McpRegistrations);
    }

    [Fact]
    public void CredentialStore_keeps_one_secret_per_tracker_url()
    {
        var store = new FakeCredentialStore();

        store.Set(AzureDevOps, "token-1");

        Assert.True(store.Exists(new Uri("https://dev.azure.com/acme")));
        Assert.Equal("token-1", store.Get(AzureDevOps));
        Assert.Null(store.Get(new Uri("https://acme.atlassian.net")));
    }

    [Fact]
    public async Task IssueReporter_records_drafts_and_returns_the_configured_url()
    {
        var reporter = new FakeIssueReporter { IssueUrl = new Uri("https://github.com/o/r/issues/7") };
        var draft = new IssueDraft("Profile for acme", "body", ["project-profile"]);

        var url = await reporter.CreateIssueAsync(draft, CancellationToken.None);

        Assert.Equal(reporter.IssueUrl, url);
        Assert.Equal([draft], reporter.Drafts);
    }
}
