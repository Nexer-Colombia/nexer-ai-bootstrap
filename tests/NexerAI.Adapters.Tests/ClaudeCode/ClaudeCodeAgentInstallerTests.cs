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

    private ClaudeCodeAgentInstaller Installer(IProcessRunner runner, string? searchPath = null) =>
        new(runner, new LocalFileSystem(), ConfigFile, WorkingDirectory, searchPath ?? Bin);
}
