using NexerAI.Adapters.ClaudeCode;
using NexerAI.Adapters.FileSystem;
using NexerAI.Adapters.Tests.Processes;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.ClaudeCode;

/// <summary>
/// The commands a channel switch runs (design 4.1), through <see cref="ClaudeCodeAgentInstaller"/> with a fake process
/// runner. Result lines were recorded on Claude Code 2.1.301 in an isolated <c>CLAUDE_CONFIG_DIR</c>, messages shortened.
/// </summary>
public sealed class ClaudeCodeSwitchCommandsTests : IDisposable
{
    private readonly TempDirectory temp = new();

    private readonly string claude;

    public ClaudeCodeSwitchCommandsTests() => claude = temp.WriteFile(@"bin\claude.exe");

    public void Dispose() => temp.Dispose();

    private string WorkingDirectory => temp.Path;

    public static TheoryData<string, string[], string> Commands => new()
    {
        {
            "disable",
            ["plugin", "disable", "nexer-core@nexer", "--scope", "local", "--json"],
            """{"command":"disable","outcome":"ok","plugin":"nexer-core@nexer","pluginId":"nexer-core@nexer","scope":"local","message":"Successfully disabled plugin: nexer-core (scope: local)"}"""
        },
        {
            "uninstall",
            ["plugin", "uninstall", "nexer-core@nexer", "--scope", "local", "--json"],
            """{"command":"uninstall","outcome":"ok","plugin":"nexer-core@nexer","pluginId":"nexer-core@nexer","scope":"local","keptData":false,"message":"Successfully uninstalled plugin: nexer-core (scope: local)"}"""
        },
        {
            "marketplace remove",
            ["plugin", "marketplace", "remove", "nexer", "--scope", "user", "--json"],
            """{"command":"marketplace-remove","outcome":"ok","marketplace":"nexer","message":"Successfully removed marketplace: nexer (from user settings)"}"""
        },
    };

    public static TheoryData<string, string> AlreadyDone => new()
    {
        { "disable", """{"command":"disable","outcome":"failed","plugin":"nexer-core@nexer","scope":"local","message":"Plugin \"nexer-core@nexer\" is already disabled at local scope","failureCode":"already_in_goal_state","alreadyInGoalState":true}""" },
        { "uninstall", """{"command":"uninstall","outcome":"failed","plugin":"nexer-core@nexer","scope":"local","message":"Plugin \"nexer-core@nexer\" is installed in user scope, not local.","failureCode":"not_installed_at_scope","installedScope":"user"}""" },
        { "uninstall", """{"command":"uninstall","outcome":"failed","plugin":"nexer-core@nexer","scope":"local","message":"Plugin \"nexer-core@nexer\" not found in installed plugins","failureCode":"not_installed"}""" },
        { "marketplace remove", """{"command":"marketplace-remove","marketplace":"nexer","outcome":"failed","message":"Marketplace 'nexer' not found","failureCode":"not_configured"}""" },
    };

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task Each_command_runs_claude_with_json_in_the_working_directory(string command, string[] arguments, string output)
    {
        var runner = new FakeProcessRunner { Result = new(0, output + "\n", string.Empty) };

        await Run(Installer(runner), command);

        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(arguments, run.Arguments);
        Assert.Null(Assert.Single(runner.Inputs));
    }

    [Theory]
    [MemberData(nameof(AlreadyDone))]
    public async Task A_command_whose_work_is_already_done_counts_as_done(string command, string output)
    {
        var runner = new FakeProcessRunner { Result = new(1, output, string.Empty) };

        await Run(Installer(runner), command);

        Assert.Single(runner.Runs);
    }

    [Theory]
    [InlineData("disable", """{"command":"disable","outcome":"failed","message":"ghp_secret","failureCode":"not_installed"}""", "failure code not_installed")]
    [InlineData("disable", """{"command":"disable","outcome":"failed","message":"ghp_secret","failureCode":"not_configured"}""", "failure code not_configured")]
    [InlineData("uninstall", """{"command":"uninstall","outcome":"failed","message":"ghp_secret","failureCode":"settings_still_on"}""", "failure code settings_still_on")]
    [InlineData("uninstall", """{"command":"uninstall","outcome":"failed","message":"ghp_secret","failureCode":"already_in_goal_state"}""", "failure code already_in_goal_state")]
    [InlineData("marketplace remove", """{"command":"marketplace-remove","outcome":"failed","message":"ghp_secret","failureCode":"not_installed"}""", "failure code not_installed")]
    [InlineData("disable", """{"command":"disable","outcome":"failed","message":"ghp_secret"}""", "could not")]
    [InlineData("uninstall", "ghp_secret", "exited with code 1")]
    public async Task Any_other_failure_is_a_typed_error_naming_the_code_but_not_the_output(string command, string output, string expected)
    {
        var runner = new FakeProcessRunner { Result = new(1, output, "ghp_secret") };

        var error = await Assert.ThrowsAsync<AgentStateException>(() => Run(Installer(runner), command));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains(command == "marketplace remove" ? "nexer" : "nexer-core@nexer", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("uninstall")]
    [InlineData("marketplace remove")]
    public async Task A_success_without_a_result_line_is_not_recognized(string command)
    {
        var runner = new FakeProcessRunner { Result = new(0, "Successfully done", string.Empty) };

        var error = await Assert.ThrowsAsync<AgentStateException>(() => Run(Installer(runner), command));

        Assert.Contains("no result line", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetPluginFoldersAsync_lists_the_existing_folders_that_hold_local_plugins()
    {
        var kept = Directory.CreateDirectory(Path.Combine(temp.Path, "repo")).FullName;
        var gone = Path.Combine(temp.Path, "deleted");
        var runner = new FakeProcessRunner
        {
            Result = new(0, $$"""
                [{"id": "a@nexer", "version": "1", "scope": "local", "enabled": false, "installPath": "x", "projectPath": "{{gone.Replace(@"\", @"\\", StringComparison.Ordinal)}}"},
                 {"id": "b@nexer", "version": "1", "scope": "user", "enabled": true, "installPath": "x"},
                 {"id": "c@other", "version": "1", "scope": "local", "enabled": false, "installPath": "x", "projectPath": "{{kept.Replace(@"\", @"\\", StringComparison.Ordinal)}}"}]
                """, string.Empty),
        };

        var folders = await Installer(runner).GetPluginFoldersAsync(TestContext.Current.CancellationToken);

        Assert.Equal([kept], folders);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(WorkingDirectory, run.WorkingDirectory);
        Assert.Equal(["plugin", "list", "--json"], run.Arguments);
    }

    [Fact]
    public async Task GetPluginFoldersAsync_reports_a_failed_command_without_its_output()
    {
        var runner = new FakeProcessRunner { Result = new(1, "ghp_secret", "ghp_secret") };

        var error = await Assert.ThrowsAsync<AgentStateException>(() => Installer(runner).GetPluginFoldersAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exited with code 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForFolder_returns_the_same_agent_acting_in_another_folder()
    {
        var other = Directory.CreateDirectory(Path.Combine(temp.Path, "other")).FullName;
        var runner = new FakeProcessRunner { Result = new(0, "[]", string.Empty) };

        await Installer(runner).ForFolder(other).GetInstalledPluginsAsync(TestContext.Current.CancellationToken);

        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, other), (run.FileName, run.WorkingDirectory));
    }

    [Fact]
    public void ForFolder_rejects_a_relative_folder()
    {
        Assert.Throws<ArgumentException>(() => Installer(new FakeProcessRunner()).ForFolder("repo"));
    }

    private static Task Run(ClaudeCodeAgentInstaller installer, string command) => command switch
    {
        "disable" => installer.DisablePluginAsync("nexer-core", "nexer", PluginScope.Local, TestContext.Current.CancellationToken),
        "uninstall" => installer.UninstallPluginAsync("nexer-core", "nexer", PluginScope.Local, TestContext.Current.CancellationToken),
        "marketplace remove" => installer.RemoveMarketplaceAsync("nexer", TestContext.Current.CancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private ClaudeCodeAgentInstaller Installer(FakeProcessRunner runner) => new(
        runner, new LocalFileSystem(), Path.Combine(temp.Path, ".claude.json"), Path.Combine(temp.Path, "settings.json"), WorkingDirectory, Path.Combine(temp.Path, "bin"));
}
