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

    [Fact]
    public void ReadMcpServers_reads_a_server_once_when_several_keys_name_the_project()
    {
        var json = """
            { "projects": {
                "C:/t/ap": { "mcpServers": { "jira": { "command": "a" } } },
                "c:\\T\\AP\\": { "mcpServers": { "jira": { "command": "b" }, "ado": { "command": "c" } } } } }
            """;

        Assert.Equal(
            [new InstalledMcpServer("jira", PluginScope.Local), new InstalledMcpServer("ado", PluginScope.Local)],
            ClaudeConfig.ReadMcpServers(json, "C:/t/ap"));
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

    [Fact]
    public void ReadMcpServers_names_the_line_of_invalid_JSON()
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudeConfig.ReadMcpServers("{\n  \"projects\": x", "C:/t/ap"));

        Assert.Contains("is not valid JSON (line 2);", error.Message, StringComparison.Ordinal);
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
    [InlineData("nexer-jira", PluginScope.Local, "cmd", new[] { "/c", "echo", "local" })]
    [InlineData("nexer-userecho", PluginScope.User, "cmd", new[] { "/c", "echo", "hi" })]
    public void RunsCommand_is_true_for_the_same_command_and_arguments_at_the_same_scope(
        string name, PluginScope scope, string command, string[] arguments)
    {
        Assert.True(ClaudeConfig.RunsCommand(Sample("claude-json-2.1.300.json"), "C:/t/ap", new(name, scope, command, arguments)));
    }

    [Theory]
    [InlineData("nexer-jira", PluginScope.Local, "CMD", new[] { "/c", "echo", "local" })]
    [InlineData("nexer-jira", PluginScope.Local, "cmd", new[] { "/c", "echo", "LOCAL" })]
    [InlineData("nexer-jira", PluginScope.Local, "cmd", new[] { "/c", "echo" })]
    [InlineData("nexer-jira", PluginScope.Local, "cmd", new[] { "/c", "echo", "local", "more" })]
    [InlineData("nexer-jira", PluginScope.User, "cmd", new[] { "/c", "echo", "local" })]
    [InlineData("nexer-userecho", PluginScope.Local, "cmd", new[] { "/c", "echo", "hi" })]
    [InlineData("nexer-other", PluginScope.Local, "cmd", new[] { "/c", "echo", "other" })]
    [InlineData("nexer-remote", PluginScope.Local, "https://example.invalid/mcp", new string[0])]
    [InlineData("nexer-missing", PluginScope.Local, "cmd", new string[0])]
    public void RunsCommand_is_false_for_another_command_line_scope_or_project_or_no_such_stdio_server(
        string name, PluginScope scope, string command, string[] arguments)
    {
        Assert.False(ClaudeConfig.RunsCommand(Sample("claude-json-2.1.300.json"), "C:/t/ap", new(name, scope, command, arguments)));
    }

    [Theory]
    [InlineData("""{"mcpServers": {"s": {"command": "x"}}}""", true)]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "args": null}}}""", false)]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "args": [1]}}}""", false)]
    [InlineData("""{"mcpServers": {"s": {"command": "x", "args": "a"}}}""", false)]
    [InlineData("""{"mcpServers": null, "projects": null}""", false)]
    [InlineData("""{"projects": {"C:/t/ap": {"mcpServers": {"s": {"command": "x"}}}}}""", false)]
    public void RunsCommand_reads_missing_arguments_as_none_and_other_shapes_as_another_command(string json, bool expected)
    {
        Assert.Equal(expected, ClaudeConfig.RunsCommand(json, "C:/t/ap", new("s", PluginScope.User, "x", [])));
    }

    [Fact]
    public void RunsCommand_is_false_when_any_key_naming_the_project_holds_another_command()
    {
        var json = """
            {"projects": {"C:/t/ap": {"mcpServers": {"s": {"command": "x", "args": []}}},
                          "c:\\T\\AP": {"mcpServers": {"s": {"command": "y", "args": []}}}}}
            """;

        Assert.False(ClaudeConfig.RunsCommand(json, "C:/t/ap", new("s", PluginScope.Local, "x", [])));
    }

    [Fact]
    public void RunsCommand_rejects_invalid_JSON_without_quoting_it()
    {
        var error = Assert.Throws<AgentStateException>(
            () => ClaudeConfig.RunsCommand("""{"mcpServers": {"s": {"env": {"T": "ghp_secret"}""", "C:/t/ap", new("s", PluginScope.User, "x", [])));

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

    [Theory]
    [InlineData(@"C:\t\ap", true)]
    [InlineData("C:/", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\server", false)]
    [InlineData(@"\\server\", false)]
    [InlineData(@"t\ap", false)]
    [InlineData("C:ap", false)]
    public void IsAbsoluteFolder_accepts_what_ProjectFolder_accepts(string path, bool expected)
    {
        Assert.Equal(expected, ClaudeConfig.IsAbsoluteFolder(path));
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
