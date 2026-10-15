using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.ClaudeCode;

/// <summary>
/// <c>claude-json-2.1.300.json</c> in <c>Samples</c> is the <c>.claude.json</c> that Claude Code 2.1.300 wrote in an
/// isolated <c>CLAUDE_CONFIG_DIR</c> after <c>claude mcp add --scope user</c> and, in <c>C:\t\ap\sub</c> (repository
/// root <c>C:\t\ap</c>) and <c>C:\t\bp</c>, <c>claude mcp add --scope local</c>. Identifiers and timestamps were
/// removed and a few unrelated fields dropped; keys, servers and shapes are as recorded.
/// </summary>
public sealed class ClaudeConfigTests
{
    private static readonly InstalledMcpServer UserEcho = new("nexer-userecho", PluginScope.User);

    [Fact]
    public void ReadMcpServers_reads_user_servers_and_the_local_servers_of_the_project()
    {
        var servers = ClaudeConfig.ReadMcpServers(Sample("claude-json-2.1.300.json"), "C:/t/ap");

        Assert.Equal(
            [UserEcho, new InstalledMcpServer("nexer-jira", PluginScope.Local), new InstalledMcpServer("nexer-remote", PluginScope.Local)],
            servers);
    }

    [Theory]
    [InlineData(@"c:\T\AP")]
    [InlineData("C:/t/ap/")]
    public void ReadMcpServers_matches_the_project_key_ignoring_case_and_separators(string projectFolder)
    {
        var servers = ClaudeConfig.ReadMcpServers(Sample("claude-json-2.1.300.json"), projectFolder);

        Assert.Contains(new InstalledMcpServer("nexer-jira", PluginScope.Local), servers);
    }

    [Theory]
    [InlineData("C:/t/cp")]
    [InlineData("C:/t/ap/sub")]
    public void ReadMcpServers_reads_only_user_servers_for_a_project_without_an_entry(string projectFolder)
    {
        Assert.Equal([UserEcho], ClaudeConfig.ReadMcpServers(Sample("claude-json-2.1.300.json"), projectFolder));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"mcpServers": null, "projects": null}""")]
    [InlineData("""{"projects": {"C:/t/ap": {"allowedTools": []}}}""")]
    [InlineData("""{"projects": {"C:/t/ap": {"mcpServers": null}, "C:/t/bp": {"mcpServers": "x"}, "C:/t/cp": 7}}""")]
    public void ReadMcpServers_reads_missing_sections_and_other_projects_as_no_servers(string json)
    {
        Assert.Empty(ClaudeConfig.ReadMcpServers(json, "C:/t/ap"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"mcpServers": []}""")]
    [InlineData("""{"mcpServers": {"a": null}}""")]
    [InlineData("""{"mcpServers": {"a": {"type": "stdio"}}}""")]
    [InlineData("""{"mcpServers": {"a": {"command": 1}}}""")]
    [InlineData("""{"projects": []}""")]
    [InlineData("""{"projects": {"C:/t/ap": []}}""")]
    [InlineData("""{"projects": {"C:/t/ap": {"mcpServers": "x"}}}""")]
    [InlineData("""{"projects": {"C:/t/ap": {"mcpServers": {"a": {"url": null}}}}}""")]
    public void ReadMcpServers_rejects_a_shape_it_does_not_recognize_with_a_typed_error(string json)
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudeConfig.ReadMcpServers(json, "C:/t/ap"));

        Assert.Contains(".claude.json", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"mcpServers": {"a": {"env": {"TOKEN": "ghp_secret"}}}}""")]
    [InlineData("""{"mcpServers": {"a": {"headers": {"Authorization": "Bearer ghp_secret"}""")]
    public void ReadMcpServers_does_not_quote_the_file_in_the_error(string json)
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudeConfig.ReadMcpServers(json, "C:/t/ap"));

        Assert.DoesNotContain("ghp_", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"C:\t\ap", "C:/t/ap")]
    [InlineData(@"C:\t\ap\sub\deeper", "C:/t/ap")]
    [InlineData(@"C:\t\wt\sub", "C:/t/wt")]
    [InlineData(@"C:\t\empty\sub", @"C:/t/empty/sub")]
    [InlineData(@"C:\t\none", "C:/t/none")]
    public void ProjectFolder_is_the_repository_root_or_else_the_working_directory(string workingDirectory, string expected)
    {
        var fileSystem = new FakeFileSystem(new Dictionary<string, string>
        {
            [@"C:\t\ap\.git\HEAD"] = "ref: refs/heads/main\n",
            [@"C:\t\wt\.git"] = "gitdir: C:/t/ap/.git/worktrees/wt\n",
            [@"C:\t\empty\.git\config"] = string.Empty, // A .git folder without HEAD is not a repository.
        });

        Assert.Equal(expected, ClaudeConfig.ProjectFolder(fileSystem, workingDirectory));
    }

    [Fact]
    public void ProjectFolder_rejects_a_relative_working_directory()
    {
        Assert.Throws<ArgumentException>(() => ClaudeConfig.ProjectFolder(new FakeFileSystem(), @"t\ap"));
    }

    [Theory]
    [InlineData(null, @"C:\Users\u\.claude.json")]
    [InlineData("", @"C:\Users\u\.claude.json")]
    [InlineData(@"C:\t\rs", @"C:\t\rs\.claude.json")]
    public void FilePath_honors_CLAUDE_CONFIG_DIR(string? configDirectory, string expected)
    {
        Assert.Equal(expected, ClaudeConfig.FilePath(configDirectory, @"C:\Users\u"));
    }

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClaudeCode", "Samples", name));
}
