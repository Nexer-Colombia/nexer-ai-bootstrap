using System.ComponentModel;
using System.Text.Json.Nodes;
using NexerAI.Adapters.ClaudeCode;
using NexerAI.Adapters.FileSystem;
using NexerAI.Adapters.Processes;
using NexerAI.Adapters.Tests.Processes;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.ClaudeCode;

/// <summary>Uses a fake process runner and a <c>.claude.json</c> in a temporary folder, never the machine's Claude Code.</summary>
public sealed class ClaudeCodeAgentInstallerTests : IDisposable
{
    /// <summary>The launcher as <c>nexer-ai project</c> registers it: forward slashes, and a user folder with a space.</summary>
    private const string LauncherPath = "C:/Users/Ana Maria/AppData/Local/nexer-ai/bin/nexer-ai.exe";

    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string ConfigFile => Path.Combine(temp.Path, ".claude.json");

    private string WorkingDirectory => Path.Combine(temp.Path, "repo", "sub");

    private string Bin => Path.Combine(temp.Path, "bin");

    [Fact]
    public async Task GetInstalledPluginsAsync_runs_claude_plugin_list_in_the_working_directory()
    {
        var runner = new FakeProcessRunner
        {
            Result = new(0, """[{"id": "nexer-core@nexer", "version": "1.0.0", "scope": "user", "enabled": false, "installPath": "x"}]""", string.Empty),
        };

        var claude = temp.WriteFile(@"bin\claude.exe");

        var plugins = await Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new InstalledPlugin("nexer-core", "nexer", "1.0.0", PluginScope.User, Enabled: false)], plugins);
        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(["plugin", "list", "--json"], run.Arguments);
    }

    [Theory]
    [InlineData(new[] { @"a\claude.cmd" }, @"a\claude.cmd")]
    [InlineData(new[] { @"a\claude.cmd", @"a\claude.exe" }, @"a\claude.exe")]
    [InlineData(new[] { @"a\claude.cmd", @"b\claude.exe" }, @"a\claude.cmd")]
    [InlineData(new[] { @"b\claude.exe", @"c\claude.exe" }, @"b\claude.exe")]
    [InlineData(new[] { @"c\claude.exe" }, @"c\claude.exe")]
    public async Task GetInstalledPluginsAsync_runs_the_first_claude_exe_or_cmd_on_PATH_folder_by_folder(string[] files, string expected)
    {
        foreach (var file in files)
        {
            temp.WriteFile(file);
        }

        var runner = new FakeProcessRunner { Result = new(0, "[]", string.Empty) };
        var searchPath = $@"relative;;{temp.Path}\a;  ""{temp.Path}\b"" ;{temp.Path}\c\";

        await Installer(runner, searchPath).GetInstalledPluginsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Path.Combine(temp.Path, expected), Assert.Single(runner.Runs).FileName);
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_ignores_a_claude_in_a_relative_PATH_entry_or_the_working_directory()
    {
        temp.WriteFile(@"bin\claude.exe");
        temp.WriteFile(@"repo\sub\claude.exe");
        var runner = new FakeProcessRunner();

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner, @"bin;.").GetInstalledPluginsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("PATH", error.Message, StringComparison.Ordinal);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_says_what_it_looked_for_and_how_to_install_when_claude_is_not_on_PATH()
    {
        var runner = new FakeProcessRunner();

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken));

        foreach (var text in new[] { "claude.exe", "claude.cmd", "PATH", "https://code.claude.com/docs/en/setup" })
        {
            Assert.Contains(text, error.Message, StringComparison.Ordinal);
        }

        Assert.Empty(runner.Runs);
    }

    [Theory]
    [InlineData(ProcessRunner.ErrorDirectory, "working directory")]
    [InlineData(ProcessRunner.ErrorFileNotFound, "no longer exists")]
    [InlineData(ProcessRunner.ErrorPathNotFound, "no longer exists")]
    [InlineData(5, "Windows error 5")]
    [InlineData(193, "Windows error 193")]
    public async Task GetInstalledPluginsAsync_tells_why_claude_cannot_be_started_from_the_error_code(int code, string expected)
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Exception = new Win32Exception(code) };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.IsType<Win32Exception>(error.InnerException);
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_reports_a_failed_command_without_its_output()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(1, "ghp_secret-out", "ghp_secret-err") };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exited with code 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMcpServersAsync_reads_the_config_file_for_the_repository_root_without_running_anything()
    {
        temp.WriteFile(@"repo\.git\HEAD", "ref: refs/heads/main\n");
        Directory.CreateDirectory(WorkingDirectory);
        var key = Path.Combine(temp.Path, "repo").Replace('\\', '/');
        File.WriteAllText(ConfigFile, $$"""
            { "mcpServers": { "github": { "type": "http", "url": "https://example.invalid/mcp" } },
              "projects": { "{{key}}": { "mcpServers": { "nexer-jira": { "type": "stdio", "command": "cmd", "args": [], "env": { } } } } } }
            """);
        var runner = new FakeProcessRunner();

        var servers = await Installer(runner).GetMcpServersAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new InstalledMcpServer("github", PluginScope.User), new InstalledMcpServer("nexer-jira", PluginScope.Local)], servers);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task GetMcpServersAsync_reads_a_missing_config_file_as_no_servers()
    {
        Assert.Empty(await Installer(new FakeProcessRunner()).GetMcpServersAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetMcpServersAsync_reports_a_config_file_it_does_not_recognize()
    {
        File.WriteAllText(ConfigFile, "{\"mcpServers\": [");

        await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(new FakeProcessRunner()).GetMcpServersAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InstallPluginAsync_runs_claude_plugin_install_with_json_in_the_working_directory()
    {
        var claude = temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(0, InstallResult("ok"), string.Empty) };

        await Installer(runner).InstallPluginAsync(Request("nexer-dev-umbraco", PluginScope.Local), TestContext.Current.CancellationToken);

        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(["plugin", "install", "nexer-dev-umbraco@nexer", "--scope", "local", "--json"], run.Arguments);
    }

    [Fact]
    public async Task InstallPluginAsync_passes_each_config_value_with_its_own_flag_ordered_by_key()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(0, InstallResult("ok", ""","configApplied":true"""), string.Empty) };
        var config = new Dictionary<string, string> { ["region"] = "eu", ["api_url"] = "https://example.invalid/a=b" };

        await Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User, config), TestContext.Current.CancellationToken);

        Assert.Equal(
            ["plugin", "install", "nexer-core@nexer", "--scope", "user", "--json", "--config", "api_url=https://example.invalid/a=b", "--config", "region=eu"],
            Assert.Single(runner.Runs).Arguments);
    }

    [Fact]
    public async Task InstallPluginAsync_accepts_a_plugin_that_is_already_installed()
    {
        temp.WriteFile(@"bin\claude.exe");
        var output = "{\"command\":\"install\",\"outcome\":\"ok\",\"message\":\"Plugin \\\"nexer-core@nexer\\\" is already installed (scope: user)\",\"installedVersion\":\"1.0.0\"}\n";
        var runner = new FakeProcessRunner { Result = new(0, output, string.Empty) };

        await Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User), TestContext.Current.CancellationToken);

        Assert.Single(runner.Runs);
    }

    [Fact]
    public async Task InstallPluginAsync_reports_a_failed_outcome_with_its_code_but_not_its_message()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner
        {
            Result = new(1, InstallResult("failed", ""","failureCode":"not_found" """), "✘ Failed: ghp_secret-err"),
        };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-qa", PluginScope.User), TestContext.Current.CancellationToken));

        Assert.Contains("nexer-qa@nexer", error.Message, StringComparison.Ordinal);
        Assert.Contains("not_found", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPluginAsync_reports_config_values_that_were_not_applied()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(0, InstallResult("ok", ""","configApplied":false"""), string.Empty) };
        var config = new Dictionary<string, string> { ["region"] = "eu" };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User, config), TestContext.Current.CancellationToken));

        Assert.Contains("configuration", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPluginAsync_does_not_fail_on_configApplied_false_when_no_config_was_passed()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(0, InstallResult("ok", ""","configApplied":false"""), string.Empty) };

        await Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User), TestContext.Current.CancellationToken);

        Assert.Single(runner.Runs);
    }

    [Fact]
    public async Task InstallPluginAsync_saves_the_config_of_an_installed_plugin_through_stdin_not_the_command_line()
    {
        // An installed plugin prints no configApplied, so install ignored --config: plugin configure --values-stdin applies it
        // (https://code.claude.com/docs/en/plugins/cli-reference#plugin-configure) without the values reaching the command line.
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new(0, InstallResult("ok", ""","installedVersion":"1.0.0" """), string.Empty));
        runner.Results.Enqueue(new(0, "{\"saved\":[\"api_url\",\"region\"]}", string.Empty));
        var config = new Dictionary<string, string> { ["region"] = "eu", ["api_url"] = "https://example.invalid/a" };

        await Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User, config), TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.Runs.Count);
        Assert.Equal(["plugin", "configure", "nexer-core@nexer", "--values-stdin", "--json"], runner.Runs[1].Arguments);
        Assert.Null(runner.Inputs[0]);
        Assert.Equal("""{"api_url":"https://example.invalid/a","region":"eu"}""", runner.Inputs[1]);
    }

    [Fact]
    public async Task InstallPluginAsync_names_the_plugin_when_saving_the_options_of_an_installed_plugin_fails()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner();
        runner.Results.Enqueue(new(0, InstallResult("ok"), string.Empty));
        runner.Results.Enqueue(new(1, "Failed to save configuration: ghp_secret", "ghp_secret-err"));
        var config = new Dictionary<string, string> { ["token"] = "ghp_secret" };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User, config), TestContext.Current.CancellationToken));

        Assert.Contains("nexer-core@nexer", error.Message, StringComparison.Ordinal);
        Assert.Contains("not applied", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a=b")]
    [InlineData("a b")]
    [InlineData("a	b")]
    public async Task InstallPluginAsync_rejects_a_config_key_that_is_empty_or_has_an_equals_sign_or_whitespace(string key)
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner();
        var config = new Dictionary<string, string> { [key] = "ghp_secret" };

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User, config), TestContext.Current.CancellationToken));

        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task InstallPluginAsync_stops_when_already_cancelled()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User), cancel.Token));

        Assert.Empty(runner.Runs);
    }

    [Theory]
    [InlineData(1, "Invalid scope: ghp_secret")]
    [InlineData(1, """{"command":"install","outcome":"ok","message":"ghp_secret"}""")]
    [InlineData(-1, "")]
    public async Task InstallPluginAsync_reports_a_failed_command_by_its_exit_code(int exitCode, string output)
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(exitCode, output, "ghp_secret-err") };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User), TestContext.Current.CancellationToken));

        Assert.Contains($"exited with code {exitCode}", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPluginAsync_reports_a_successful_exit_without_a_result_line()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner { Result = new(0, "Successfully installed plugin: ghp_secret", string.Empty) };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User), TestContext.Current.CancellationToken));

        Assert.Contains("does not recognize", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallPluginAsync_says_how_to_install_claude_when_it_is_not_on_PATH()
    {
        var runner = new FakeProcessRunner();

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).InstallPluginAsync(Request("nexer-core", PluginScope.User), TestContext.Current.CancellationToken));

        Assert.Contains("https://code.claude.com/docs/en/setup", error.Message, StringComparison.Ordinal);
        Assert.Empty(runner.Runs);
    }

    [Theory]
    [InlineData(PluginScope.Local, "local")]
    [InlineData(PluginScope.User, "user")]
    public async Task AddMcpServerAsync_runs_claude_mcp_add_for_a_new_server(PluginScope scope, string scopeName)
    {
        var claude = temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner();

        await Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", scope), TestContext.Current.CancellationToken);

        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(
            ["mcp", "add", "--transport", "stdio", "--scope", scopeName, "nexer-jira", "--", LauncherPath, "mcp-launch", "--type", "jira"],
            run.Arguments);
    }

    [Fact]
    public async Task AddMcpServerAsync_does_nothing_when_the_server_already_runs_the_same_command()
    {
        temp.WriteFile(@"bin\claude.exe");
        WriteLocalServer("nexer-jira", LauncherPath, ["mcp-launch", "--type", "jira"]);
        var runner = new FakeProcessRunner();

        await Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", PluginScope.Local), TestContext.Current.CancellationToken);

        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task AddMcpServerAsync_removes_and_adds_again_a_server_with_another_command()
    {
        temp.WriteFile(@"bin\claude.exe");
        WriteLocalServer("nexer-jira", "C:/old/nexer-ai.exe", ["mcp-launch", "--type", "jira"]);
        var runner = new FakeProcessRunner();

        await Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", PluginScope.Local), TestContext.Current.CancellationToken);

        Assert.Equal(2, runner.Runs.Count);
        Assert.Equal(["mcp", "remove", "--scope", "local", "nexer-jira"], runner.Runs[0].Arguments);
        Assert.Equal("add", runner.Runs[1].Arguments[1]);
        Assert.All(runner.Runs, run => Assert.Equal(WorkingDirectory, run.WorkingDirectory));
    }

    [Fact]
    public async Task AddMcpServerAsync_adds_without_removing_when_the_name_exists_only_at_the_other_scope()
    {
        temp.WriteFile(@"bin\claude.exe");
        File.WriteAllText(ConfigFile, """{ "mcpServers": { "nexer-jira": { "type": "stdio", "command": "x", "args": [] } } }""");
        var runner = new FakeProcessRunner();

        await Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", PluginScope.Local), TestContext.Current.CancellationToken);

        Assert.Equal("add", Assert.Single(runner.Runs).Arguments[1]);
    }

    [Fact]
    public async Task AddMcpServerAsync_stops_when_the_old_server_cannot_be_removed()
    {
        temp.WriteFile(@"bin\claude.exe");
        WriteLocalServer("nexer-jira", "C:/old/nexer-ai.exe", []);
        var runner = new FakeProcessRunner { Result = new(1, string.Empty, "No MCP server named \"nexer-jira\" ghp_secret") };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", PluginScope.Local), TestContext.Current.CancellationToken));

        Assert.Contains("'claude mcp remove --scope local nexer-jira' exited with code 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
        Assert.Single(runner.Runs);
    }

    [Fact]
    public async Task AddMcpServerAsync_reports_a_failed_add_without_its_output()
    {
        temp.WriteFile(@"bin\claude.exe");
        var runner = new FakeProcessRunner
        {
            Results = new([new(1, "Added stdio MCP server with command: x ghp_secret-out", "ghp_secret-err")]),
        };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", PluginScope.Local), TestContext.Current.CancellationToken));

        Assert.Contains("'claude mcp add --scope local nexer-jira' exited with code 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMcpServerAsync_runs_nothing_when_the_config_file_is_not_recognized()
    {
        temp.WriteFile(@"bin\claude.exe");
        File.WriteAllText(ConfigFile, "{\"mcpServers\": [");
        var runner = new FakeProcessRunner();

        await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).AddMcpServerAsync(Launcher("nexer-jira", PluginScope.Local), TestContext.Current.CancellationToken));

        Assert.Empty(runner.Runs);
    }

    [Fact]
    public void Constructor_rejects_relative_paths()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeCodeAgentInstaller(new FakeProcessRunner(), new LocalFileSystem(), ".claude.json", WorkingDirectory, Bin));
        Assert.Throws<ArgumentException>(() => new ClaudeCodeAgentInstaller(new FakeProcessRunner(), new LocalFileSystem(), ConfigFile, "repo", Bin));
    }

    [Theory]
    [InlineData(@"\\server")]
    [InlineData(@"\\server\")]
    [InlineData("//server")]
    public void Constructor_rejects_a_working_directory_without_a_UNC_share(string workingDirectory)
    {
        Assert.Throws<ArgumentException>(() => new ClaudeCodeAgentInstaller(new FakeProcessRunner(), new LocalFileSystem(), ConfigFile, workingDirectory, Bin));
    }

    private static McpServerRegistration Launcher(string name, PluginScope scope) =>
        new(name, scope, LauncherPath, ["mcp-launch", "--type", "jira"]);

    /// <summary>Writes <c>.claude.json</c> with one local server of the repository root, as Claude Code 2.1.300 writes it.</summary>
    private void WriteLocalServer(string name, string command, string[] arguments)
    {
        temp.WriteFile(@"repo\.git\HEAD", "ref: refs/heads/main\n");
        var key = Path.Combine(temp.Path, "repo").Replace('\\', '/');
        var json = new JsonObject
        {
            ["projects"] = new JsonObject
            {
                [key] = new JsonObject
                {
                    ["mcpServers"] = new JsonObject
                    {
                        [name] = new JsonObject
                        {
                            ["type"] = "stdio",
                            ["command"] = command,
                            ["args"] = new JsonArray([.. arguments.Select(a => JsonValue.Create(a))]),
                            ["env"] = new JsonObject(),
                        },
                    },
                },
            },
        };
        File.WriteAllText(ConfigFile, json.ToJsonString());
    }

    /// <summary>A result line as Claude Code 2.1.300 prints it, with <paramref name="extra"/> fields appended.</summary>
    private static string InstallResult(string outcome, string extra = "") =>
        $$"""{"command":"install","outcome":"{{outcome}}","message":"ghp_secret-in-message"{{extra}}}""" + "\n";

    private static PluginInstallRequest Request(string name, PluginScope scope, Dictionary<string, string>? config = null) =>
        new(name, "nexer", scope, config ?? []);

    private ClaudeCodeAgentInstaller Installer(IProcessRunner runner, string? searchPath = null) =>
        new(runner, new LocalFileSystem(), ConfigFile, WorkingDirectory, searchPath ?? Bin);
}
