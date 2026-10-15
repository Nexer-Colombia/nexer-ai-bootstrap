using NexerAI.Adapters.ClaudeCode;
using NexerAI.Adapters.FileSystem;
using NexerAI.Adapters.Tests.Processes;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.ClaudeCode;

/// <summary>
/// Channel registration through <see cref="ClaudeCodeAgentInstaller"/>, with a fake process runner and the files in a
/// temporary folder, never the machine's Claude Code. Result lines were recorded on Claude Code 2.1.301.
/// </summary>
public sealed class ClaudeCodeMarketplaceTests : IDisposable
{
    private readonly TempDirectory temp = new();

    private readonly string claude;

    public ClaudeCodeMarketplaceTests() => claude = temp.WriteFile(@"bin\claude.exe");

    public void Dispose() => temp.Dispose();

    private string WorkingDirectory => temp.Path;

    private string SettingsFile => Path.Combine(temp.Path, "config", "settings.json");

    [Fact]
    public async Task GetMarketplacesAsync_runs_claude_plugin_marketplace_list_in_the_working_directory()
    {
        var runner = new FakeProcessRunner
        {
            Result = new(0, """[{"name": "nexer", "source": "github", "repo": "Nexer-Colombia/nexer-ai-marketplace", "installLocation": "C:\\x"}]""", string.Empty),
        };

        var marketplaces = await Installer(runner).GetMarketplacesAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new RegisteredMarketplace("nexer", "github", "Nexer-Colombia/nexer-ai-marketplace", null)], marketplaces);
        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(["plugin", "marketplace", "list", "--json"], run.Arguments);
        Assert.Null(Assert.Single(runner.Inputs));
    }

    [Fact]
    public async Task GetMarketplacesAsync_reports_a_failed_command_without_its_output()
    {
        var runner = new FakeProcessRunner { Result = new(1, "ghp_secret-out", "ghp_secret-err") };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).GetMarketplacesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("exited with code 1", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddMarketplaceAsync_adds_the_channel_repository_at_user_scope()
    {
        var runner = new FakeProcessRunner
        {
            Result = new(0, "Cloning\n" + """{"command":"marketplace-add","outcome":"ok","marketplace":"nexer-early","message":"Successfully added marketplace: nexer-early (declared in user settings)"}""", string.Empty),
        };

        await Installer(runner).AddMarketplaceAsync(Channel.Early, TestContext.Current.CancellationToken);

        var run = Assert.Single(runner.Runs);
        Assert.Equal((claude, WorkingDirectory), (run.FileName, run.WorkingDirectory));
        Assert.Equal(["plugin", "marketplace", "add", "Nexer-Colombia/nexer-ai-marketplace-early", "--scope", "user", "--json"], run.Arguments);
    }

    [Theory]
    [InlineData(1, """{"command":"marketplace-add","outcome":"failed","message":"ghp_secret","failureCode":"invalid_source"}""", "failure code invalid_source")]
    [InlineData(0, """{"command":"marketplace-add","outcome":"failed","message":"ghp_secret"}""", "could not add")]
    [InlineData(1, "ghp_secret", "exited with code 1")]
    [InlineData(0, "ghp_secret", "no result line")]
    [InlineData(0, """{"command":"marketplace-add","outcome":"ok","marketplace":"ghp_secret","message":"m"}""", "another name")]
    public async Task AddMarketplaceAsync_reports_a_failure_without_quoting_the_output(int exitCode, string output, string expected)
    {
        var runner = new FakeProcessRunner { Result = new(exitCode, output, "ghp_secret") };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(runner).AddMarketplaceAsync(Channel.Stable, TestContext.Current.CancellationToken));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("Nexer-Colombia/nexer-ai-marketplace", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnableMarketplaceAutoUpdateAsync_writes_the_setting_into_the_user_settings_file_without_running_anything()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, """{"extraKnownMarketplaces": {"nexer": {"source": {"source": "github", "repo": "Nexer-Colombia/nexer-ai-marketplace"}}}, "theme": "dark"}""");
        var runner = new FakeProcessRunner();

        await Installer(runner).EnableMarketplaceAutoUpdateAsync(Channel.Stable, TestContext.Current.CancellationToken);

        var text = File.ReadAllText(SettingsFile);
        Assert.Contains("\"autoUpdate\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"theme\": \"dark\"", text, StringComparison.Ordinal);
        Assert.Empty(runner.Runs);
    }

    [Fact]
    public async Task EnableMarketplaceAutoUpdateAsync_leaves_a_malformed_file_as_it_is()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, "{ // ghp_secret\n}");

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => Installer(new FakeProcessRunner()).EnableMarketplaceAutoUpdateAsync(Channel.Stable, TestContext.Current.CancellationToken));

        Assert.Contains(SettingsFile, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("{ // ghp_secret\n}", File.ReadAllText(SettingsFile));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(SettingsFile)!));
    }

    [Fact]
    public void Constructor_rejects_a_relative_settings_file()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeCodeAgentInstaller(
            new FakeProcessRunner(), new LocalFileSystem(), Path.Combine(temp.Path, ".claude.json"), "settings.json", WorkingDirectory, temp.Path));
    }

    private ClaudeCodeAgentInstaller Installer(FakeProcessRunner runner) =>
        new(runner, new LocalFileSystem(), Path.Combine(temp.Path, ".claude.json"), SettingsFile, WorkingDirectory, Path.Combine(temp.Path, "bin"));
}
