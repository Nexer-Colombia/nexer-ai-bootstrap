using NexerAI.Core.Domain;
using NexerAI.Core.Planning;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.Planning;

public sealed class InstallPlannerTests
{
    private static readonly Dictionary<string, string> NoConfig = [];

    private static readonly PluginInstallRequest Core = Request("nexer-core", PluginScope.User);

    private static readonly PluginInstallRequest Qa = Request("nexer-qa", PluginScope.User);

    private static readonly PluginInstallRequest Mod = Request("nexer-mod", PluginScope.User);

    private static readonly PluginInstallRequest Umbraco = Request("nexer-dev-umbraco", PluginScope.Local);

    private static readonly PluginInstallRequest Engram = Request("nexer-engram", PluginScope.Local);

    private static readonly McpServerRegistration Jira = Server("nexer-jira");

    private static readonly McpServerRegistration AzureDevOps = Server("nexer-azure-devops");

    [Fact]
    public void Plan_installs_everything_in_a_fixed_order_when_nothing_is_installed()
    {
        var desired = new DesiredState([Engram, Mod, Umbraco, Qa, Core], [Jira, AzureDevOps]);

        var plan = InstallPlanner.Plan(desired, [], []);

        Assert.Equal<PlanAction>(
            [
                new PlanAction.InstallPlugin(Core),
                new PlanAction.InstallPlugin(Mod),
                new PlanAction.InstallPlugin(Qa),
                new PlanAction.InstallPlugin(Umbraco),
                new PlanAction.InstallPlugin(Engram),
                new PlanAction.AddMcpServer(AzureDevOps),
                new PlanAction.AddMcpServer(Jira),
            ],
            plan);
    }

    [Fact]
    public void Plan_is_empty_when_the_actual_state_matches_whatever_the_versions()
    {
        var desired = new DesiredState([Core, Umbraco], [Jira]);
        InstalledPlugin[] plugins =
        [
            new("nexer-dev-umbraco", "nexer", "0.1.0", PluginScope.Local),
            new("nexer-core", "nexer", "9.9.9", PluginScope.User),
            new("nexer-other", "nexer", "1.0.0", PluginScope.User),
        ];

        var plan = InstallPlanner.Plan(desired, plugins, [new("nexer-jira", PluginScope.Local), new("github", PluginScope.User)]);

        Assert.Empty(plan);
    }

    [Theory]
    [InlineData("nexer-core", "nexer-early", PluginScope.User)]
    [InlineData("nexer-core", "nexer", PluginScope.Local)]
    [InlineData("Nexer-Core", "nexer", PluginScope.User)]
    [InlineData("nexer-core", "Nexer", PluginScope.User)]
    public void Plan_installs_a_plugin_installed_under_another_name_marketplace_or_scope(string name, string marketplace, PluginScope scope)
    {
        var plan = InstallPlanner.Plan(new DesiredState([Core], []), [new(name, marketplace, "1.0.0", scope)], []);

        Assert.Equal([new PlanAction.InstallPlugin(Core)], plan);
    }

    [Fact]
    public void Plan_adds_an_MCP_server_present_only_at_another_scope_or_under_another_name()
    {
        InstalledMcpServer[] servers = [new("nexer-jira", PluginScope.User), new("Nexer-Azure-DevOps", PluginScope.Local)];

        var plan = InstallPlanner.Plan(new DesiredState([], [Jira, AzureDevOps]), [], servers);

        Assert.Equal<PlanAction>([new PlanAction.AddMcpServer(AzureDevOps), new PlanAction.AddMcpServer(Jira)], plan);
    }

    [Fact]
    public void Plan_lists_each_desired_entry_once()
    {
        var desired = new DesiredState([Umbraco, Request("nexer-dev-umbraco", PluginScope.Local), Umbraco], [Jira, Server("nexer-jira")]);

        var plan = InstallPlanner.Plan(desired, [], []);

        Assert.Equal<PlanAction>([new PlanAction.InstallPlugin(Umbraco), new PlanAction.AddMcpServer(Jira)], plan);
    }

    [Fact]
    public void Plan_rejects_null_arguments()
    {
        var desired = new DesiredState([], []);

        Assert.Throws<ArgumentNullException>(() => InstallPlanner.Plan(null!, [], []));
        Assert.Throws<ArgumentNullException>(() => InstallPlanner.Plan(desired, null!, []));
        Assert.Throws<ArgumentNullException>(() => InstallPlanner.Plan(desired, [], null!));
    }

    [Fact]
    public async Task PlanAsync_reads_the_actual_state_from_the_agent_and_changes_nothing()
    {
        var installer = new FakeAgentInstaller
        {
            Installed = [new InstalledPlugin("nexer-core", "nexer", "1.2.0", PluginScope.User)],
            InstalledMcpServers = [new InstalledMcpServer("nexer-jira", PluginScope.Local)],
        };
        var ct = TestContext.Current.CancellationToken;

        var plan = await InstallPlanner.PlanAsync(new DesiredState([Core, Qa], [Jira, AzureDevOps]), installer, ct);

        Assert.Equal<PlanAction>([new PlanAction.InstallPlugin(Qa), new PlanAction.AddMcpServer(AzureDevOps)], plan);
        Assert.Empty(installer.InstallRequests);
        Assert.Empty(installer.McpRegistrations);
        await Assert.ThrowsAsync<ArgumentNullException>(() => InstallPlanner.PlanAsync(null!, installer, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => InstallPlanner.PlanAsync(new DesiredState([], []), null!, ct));
    }

    private static PluginInstallRequest Request(string name, PluginScope scope) => new(name, "nexer", scope, NoConfig);

    private static McpServerRegistration Server(string name) =>
        new(name, PluginScope.Local, @"C:\bin\nexer-ai.exe", ["mcp-launch", "--type", name["nexer-".Length..]]);
}
