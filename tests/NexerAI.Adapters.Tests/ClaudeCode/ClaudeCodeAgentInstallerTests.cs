using System.ComponentModel;
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
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string ConfigFile => Path.Combine(temp.Path, ".claude.json");

    private string WorkingDirectory => Path.Combine(temp.Path, "repo", "sub");

    [Fact]
    public async Task GetInstalledPluginsAsync_runs_claude_plugin_list_in_the_working_directory()
    {
        var runner = new FakeProcessRunner
        {
            Result = new(0, """[{"id": "nexer-core@nexer", "version": "1.0.0", "scope": "user", "enabled": false, "installPath": "x"}]""", string.Empty),
        };

        var plugins = await Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new InstalledPlugin("nexer-core", "nexer", "1.0.0", PluginScope.User, Enabled: false)], plugins);
        var run = Assert.Single(runner.Runs);
        Assert.Equal(("claude", WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(["plugin", "list", "--json"], run.Arguments);
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_reports_a_failed_command_without_its_output()
    {
        var runner = new FakeProcessRunner { Result = new(1, "ghp_secret-out", "ghp_secret-err") };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exited with code 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_reports_that_claude_cannot_be_started()
    {
        var runner = new FakeProcessRunner { Exception = new Win32Exception(2) };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).GetInstalledPluginsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("PATH", error.Message, StringComparison.Ordinal);
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
    public void Constructor_rejects_relative_paths()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeCodeAgentInstaller(new FakeProcessRunner(), new LocalFileSystem(), ".claude.json", WorkingDirectory));
        Assert.Throws<ArgumentException>(() => new ClaudeCodeAgentInstaller(new FakeProcessRunner(), new LocalFileSystem(), ConfigFile, "repo"));
    }

    private ClaudeCodeAgentInstaller Installer(IProcessRunner runner) =>
        new(runner, new LocalFileSystem(), ConfigFile, WorkingDirectory);
}
