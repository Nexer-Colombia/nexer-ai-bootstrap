using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.ClaudeCode;

/// <summary>
/// <c>plugin-list-2.1.300.json</c> in <c>Samples</c> was recorded from <c>claude plugin list --json</c> on Claude
/// Code 2.1.300 in <c>C:\t\ap\sub</c>, with an isolated <c>CLAUDE_CONFIG_DIR</c> and a throwaway local marketplace
/// <c>nexer</c>. A few unrelated fields were dropped; ids, scopes, flags and paths are as recorded. <c>nexer-qa</c>
/// is installed at user scope and disabled, and at local scope in another folder (<c>C:\t\bp</c>).
/// </summary>
public sealed class ClaudePluginListTests
{
    [Fact]
    public void Parse_reads_the_recorded_list_with_the_local_plugins_of_the_working_directory_only()
    {
        var plugins = ClaudePluginList.Parse(Sample("plugin-list-2.1.300.json"), @"C:\t\ap\sub");

        Assert.Equal(
            [
                new InstalledPlugin("nexer-core", "nexer", "1.0.0", PluginScope.User, Enabled: true),
                new InstalledPlugin("nexer-dev-umbraco", "nexer", "1.0.0", PluginScope.Local, Enabled: true),
                new InstalledPlugin("nexer-qa", "nexer", "1.0.0", PluginScope.User, Enabled: false),
            ],
            plugins);
    }

    [Theory]
    [InlineData("c:/T/AP/sub/")]
    [InlineData(@"C:\t\ap\.\sub")]
    public void Parse_matches_the_project_path_ignoring_case_and_separators(string workingDirectory)
    {
        var plugins = ClaudePluginList.Parse(Sample("plugin-list-2.1.300.json"), workingDirectory);

        Assert.Contains(plugins, p => p.Name == "nexer-dev-umbraco" && p.Scope == PluginScope.Local);
    }

    [Fact]
    public void Parse_skips_a_local_plugin_of_the_repository_root_when_running_in_a_subfolder()
    {
        // Observed on 2.1.300: a local plugin installed in C:\t\ap\sub is not enabled in C:\t\ap, and the reverse.
        var plugins = ClaudePluginList.Parse(Sample("plugin-list-2.1.300.json"), @"C:\t\ap");

        Assert.DoesNotContain(plugins, p => p.Scope == PluginScope.Local);
    }

    [Theory]
    [InlineData("""{"id": "a@m", "version": "1", "scope": "project", "enabled": true}""")]
    [InlineData("""{"id": "a@m", "version": "1", "scope": "managed", "enabled": true}""")]
    [InlineData("""{"id": "a@inline", "version": "1", "scope": "session", "enabled": true}""")]
    [InlineData("""{"id": "a@m", "version": "1", "scope": "local", "enabled": true}""")]
    [InlineData("""{"id": "no-marketplace", "version": "1", "scope": "user", "enabled": true}""")]
    [InlineData("""{"id": "@m", "version": "1", "scope": "user", "enabled": true}""")]
    [InlineData("""{"id": "a@", "version": "1", "scope": "user", "enabled": true}""")]
    public void Parse_skips_entries_the_bootstrap_does_not_manage(string entry)
    {
        Assert.Empty(ClaudePluginList.Parse($"[{entry}]", @"C:\t\ap"));
    }

    [Theory]
    [InlineData("""{"scope": "managed"}""")]
    [InlineData("""{"id": 7, "scope": "project", "enabled": "yes"}""")]
    [InlineData("""{"id": "a@m", "scope": "session"}""")]
    [InlineData("""{"version": "1", "scope": "local", "projectPath": "C:/t/bp", "enabled": true}""")]
    public void Parse_skips_an_incomplete_entry_at_a_scope_or_folder_it_does_not_use(string entry)
    {
        var plugins = ClaudePluginList.Parse($$"""[{{entry}}, {"id": "a@m", "version": "1", "scope": "user", "enabled": true}]""", @"C:\t\ap");

        Assert.Equal([new InstalledPlugin("a", "m", "1", PluginScope.User, Enabled: true)], plugins);
    }

    [Fact]
    public void Parse_reads_an_empty_list()
    {
        Assert.Empty(ClaudePluginList.Parse("[]\n", @"C:\t\ap"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("No plugins installed")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("""[{"version": "1", "scope": "user", "enabled": true}]""")]
    [InlineData("""[{"id": "a@m", "scope": "user", "enabled": true}]""")]
    [InlineData("""[{"id": "a@m", "version": "1", "enabled": true}]""")]
    [InlineData("""[{"id": "a@m", "version": "1", "scope": "user"}]""")]
    [InlineData("""[{"id": "a@m", "version": "1", "scope": "user", "enabled": "true"}]""")]
    [InlineData("""[{"id": 7, "version": "1", "scope": "user", "enabled": true}]""")]
    [InlineData("""[{"id": "a@m", "version": "1", "enabled": true, "scope": 1}]""")]
    [InlineData("""[{"id": "a@m", "scope": "local", "projectPath": "C:/t/ap", "enabled": true}]""")]
    public void Parse_rejects_output_it_does_not_recognize_with_a_typed_error(string json)
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudePluginList.Parse(json, @"C:\t\ap"));

        Assert.Contains("claude plugin list --json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_does_not_quote_the_output_in_the_error()
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudePluginList.Parse("[{\"id\": \"secret-ghp_x", @"C:\t\ap"));

        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LocalFolders_lists_each_folder_with_local_plugins_once_in_output_order()
    {
        // Recorded on 2.1.301 from C:\t\pb: nexer-core@nexer at local scope in C:\t\pa (enabled there) and C:\t\pc
        // (disabled there). Both report enabled false, the state in C:\t\pb, not in their own folder.
        Assert.Equal([@"C:\t\pa", @"C:\t\pc"], ClaudePluginList.LocalFolders(Sample("plugin-list-folders-2.1.301.json")));
        Assert.Equal([@"C:\t\ap\sub", @"C:\t\bp"], ClaudePluginList.LocalFolders(Sample("plugin-list-2.1.300.json")));
    }

    [Fact]
    public void LocalFolders_merges_spellings_of_one_folder_and_skips_entries_without_an_absolute_project_path()
    {
        var folders = ClaudePluginList.LocalFolders("""
            [
              {"id": "a@m", "scope": "local", "projectPath": "C:\\t\\pa"},
              {"id": "b@m", "scope": "local", "projectPath": "c:/T/PA/"},
              {"id": "c@m", "scope": "local", "projectPath": "relative"},
              {"id": "d@m", "scope": "local"},
              {"id": "e@m", "scope": "project", "projectPath": "C:\\t\\pp"},
              {"id": "f@m", "scope": "user"}
            ]
            """);

        Assert.Equal([@"C:\t\pa"], folders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("""[{"id": "a@m"}]""")]
    public void LocalFolders_rejects_output_it_does_not_recognize_with_a_typed_error(string json)
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudePluginList.LocalFolders(json));

        Assert.Contains("claude plugin list --json", error.Message, StringComparison.Ordinal);
    }

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClaudeCode", "Samples", name));
}
