using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.ClaudeCode;

/// <summary>
/// <c>marketplace-list-2.1.301.json</c> in <c>Samples</c> was recorded from <c>claude plugin marketplace list --json</c>
/// on Claude Code 2.1.301, with an isolated <c>CLAUDE_CONFIG_DIR</c> (<c>C:\t\cr</c>), after
/// <c>claude plugin marketplace add</c> of both channel repositories.
/// </summary>
public sealed class ClaudeMarketplaceListTests
{
    [Fact]
    public void Parse_reads_the_recorded_list()
    {
        var marketplaces = ClaudeMarketplaceList.Parse(Sample("marketplace-list-2.1.301.json"));

        Assert.Equal(
            [
                new RegisteredMarketplace("nexer", "github", "Nexer-Colombia/nexer-ai-marketplace", null),
                new RegisteredMarketplace("nexer-early", "github", "Nexer-Colombia/nexer-ai-marketplace-early", null),
            ],
            marketplaces);
    }

    [Fact]
    public void Parse_reads_the_documented_fields_of_other_sources_and_a_pinned_ref()
    {
        var marketplaces = ClaudeMarketplaceList.Parse("""
            [
              {"name": "a", "source": "git", "url": "https://example.invalid/a.git", "ref": "v1", "installLocation": "C:\\x"},
              {"name": "b", "source": "directory", "path": "C:\\m"},
              {"name": "c", "source": "github", "repo": "o/c", "ref": "main"},
              {"name": "d", "source": "claudeai", "marketplaceId": "m", "organizationUuid": "u", "status": "ok"}
            ]
            """);

        Assert.Equal(
            [
                new RegisteredMarketplace("a", "git", null, "v1"),
                new RegisteredMarketplace("b", "directory", null, null),
                new RegisteredMarketplace("c", "github", "o/c", "main"),
                new RegisteredMarketplace("d", "claudeai", null, null),
            ],
            marketplaces);
    }

    [Fact]
    public void Parse_reads_an_empty_list()
    {
        Assert.Empty(ClaudeMarketplaceList.Parse("[]\n"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("No marketplaces configured")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("""[{"source": "github", "repo": "o/r"}]""")]
    [InlineData("""[{"name": "a", "repo": "o/r"}]""")]
    [InlineData("""[{"name": 1, "source": "github"}]""")]
    [InlineData("""[{"name": "a", "source": "github", "repo": 7}]""")]
    [InlineData("""[{"name": "a", "source": "github", "ref": false}]""")]
    public void Parse_rejects_output_it_does_not_recognize_with_a_typed_error(string json)
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudeMarketplaceList.Parse(json));

        Assert.Contains("claude plugin marketplace list --json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_does_not_quote_the_output_in_the_error()
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudeMarketplaceList.Parse("[{\"name\": \"secret-ghp_x"));

        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_channel_is_registered_only_by_its_unpinned_github_repository_under_its_name()
    {
        var registered = new RegisteredMarketplace("nexer", "github", "Nexer-Colombia/nexer-ai-marketplace", null);

        Assert.True(registered.IsRegistrationOf(Channel.Stable));
        Assert.False(registered.IsRegistrationOf(Channel.Early));
        Assert.False((registered with { Ref = "stable" }).IsRegistrationOf(Channel.Stable));
        Assert.False((registered with { Repository = "nexer-colombia/nexer-ai-marketplace" }).IsRegistrationOf(Channel.Stable));
        Assert.False((registered with { Source = "git", Repository = null }).IsRegistrationOf(Channel.Stable));
    }

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClaudeCode", "Samples", name));
}
