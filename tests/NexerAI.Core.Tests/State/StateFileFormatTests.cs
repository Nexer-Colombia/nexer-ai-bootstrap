using System.Text.Json;
using NexerAI.Core.Domain;
using NexerAI.Core.State;

namespace NexerAI.Core.Tests.State;

public sealed class StateFileFormatTests
{
    /// <summary>The design example, as the serializer writes it.</summary>
    private static readonly string DesignExample = """
        {
          "schema": 1,
          "channel": "nexer",
          "role": "qa",
          "installed": {
            "nexer-core": "1.2.0",
            "nexer-qa": "0.4.0"
          },
          "projects": {
            "github.com/acme/website": {
              "profile": "acme-website",
              "plugins": [
                "nexer-dev-umbraco",
                "nexer-engram"
              ],
              "mcp": [
                "nexer-azure-devops"
              ]
            }
          }
        }
        """.ReplaceLineEndings("\n") + "\n";

    private static readonly ProjectState Website = new("acme-website", ["nexer-dev-umbraco", "nexer-engram"], ["nexer-azure-devops"]);

    private static InstallState DesignState() => InstallState.Empty
        .WithProject("github.com/acme/website", Website) with
    {
        Channel = Channel.Stable,
        Role = Role.Qa,
        InstalledPlugins = new Dictionary<string, string> { ["nexer-qa"] = "0.4.0", ["nexer-core"] = "1.2.0" },
    };

    [Fact]
    public void Serialize_writes_the_design_example_byte_for_byte()
    {
        Assert.Equal(DesignExample, StateFileFormat.Serialize(DesignState()));
    }

    [Fact]
    public void Deserialize_reads_the_design_example_into_domain_types()
    {
        var state = StateFileFormat.Deserialize(DesignExample);

        Assert.Same(Channel.Stable, state.Channel);
        Assert.Equal(Role.Qa, state.Role);
        Assert.Equal(new Dictionary<string, string> { ["nexer-core"] = "1.2.0", ["nexer-qa"] = "0.4.0" }, state.InstalledPlugins);
        var (key, project) = Assert.Single(state.Projects);
        Assert.Equal("github.com/acme/website", key);
        Assert.Equal("acme-website", project.Profile);
        Assert.Equal(["nexer-dev-umbraco", "nexer-engram"], project.Plugins);
        Assert.Equal(["nexer-azure-devops"], project.McpServers);
        Assert.Equal(DesignExample, StateFileFormat.Serialize(state));
    }

    [Fact]
    public void Serialize_orders_dictionary_keys_ordinally_and_keeps_list_order()
    {
        var project = new ProjectState("p", ["z-plugin", "a-plugin"], ["nexer-jira", "nexer-azure-devops"]);
        var state = InstallState.Empty
            .WithProject("github.com/acme/b", project)
            .WithProject("github.com/acme/B", project)
            .WithProject("github.com/acme/a", project) with
        {
            InstalledPlugins = new Dictionary<string, string> { ["nexer-qa"] = "1", ["Nexer-x"] = "1", ["nexer-core"] = "1" },
        };

        var json = StateFileFormat.Serialize(state);

        AssertInOrder(json, "\"Nexer-x\"", "\"nexer-core\"", "\"nexer-qa\"");
        AssertInOrder(json, "\"github.com/acme/B\"", "\"github.com/acme/a\"", "\"github.com/acme/b\"");
        AssertInOrder(json, "\"z-plugin\"", "\"a-plugin\"", "\"nexer-jira\"", "\"nexer-azure-devops\"");
    }

    [Fact]
    public void Serialize_writes_the_empty_state_without_channel_and_role()
    {
        var expected = "{\n  \"schema\": 1,\n  \"installed\": {},\n  \"projects\": {}\n}\n";

        Assert.Equal(expected, StateFileFormat.Serialize(InstallState.Empty));
        Assert.Equal(expected, StateFileFormat.Serialize(StateFileFormat.Deserialize(expected)));
    }

    [Fact]
    public void Deserialize_treats_missing_or_null_sections_as_empty()
    {
        var state = StateFileFormat.Deserialize("""{ "schema": 1, "channel": null, "installed": null }""");

        Assert.Null(state.Channel);
        Assert.Null(state.Role);
        Assert.Empty(state.InstalledPlugins);
        Assert.Empty(state.Projects);
    }

    [Fact]
    public void Deserialize_treats_missing_project_lists_as_empty()
    {
        var state = StateFileFormat.Deserialize("""{ "schema": 1, "projects": { "github.com/acme/api": { "profile": "acme-api" } } }""");

        var project = state.Projects["github.com/acme/api"];
        Assert.Equal("acme-api", project.Profile);
        Assert.Empty(project.Plugins);
        Assert.Empty(project.McpServers);
    }

    [Fact]
    public void Deserialize_ignores_unknown_properties()
    {
        var state = StateFileFormat.Deserialize("""
            {
              "schema": 1,
              "channel": "nexer-early",
              "lastRun": "2026-10-09",
              "extra": { "nested": [1, 2] },
              "projects": { "github.com/acme/api": { "profile": "acme-api", "note": "x" } }
            }
            """);

        Assert.Same(Channel.Early, state.Channel);
        Assert.Equal("acme-api", state.Projects["github.com/acme/api"].Profile);
    }

    [Theory]
    [InlineData("developer", Role.Developer)]
    [InlineData("qa", Role.Qa)]
    [InlineData("po", Role.ProductOwner)]
    public void Deserialize_reads_every_role_token(string token, Role role)
    {
        var state = StateFileFormat.Deserialize($$"""{ "schema": 1, "role": "{{token}}" }""");

        Assert.Equal(role, state.Role);
        Assert.Contains($"\"role\": \"{token}\"", StateFileFormat.Serialize(state), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{}""", "no 'schema'")]
    [InlineData("""{ "schema": null }""", "no 'schema'")]
    [InlineData("""{ "schema": 2 }""", "schema version 2")]
    [InlineData("""{ "schema": 0 }""", "schema version 0")]
    [InlineData("""{ "schema": "1" }""", "schema version \"1\"")]
    [InlineData("""{ "schema": 1.0 }""", "schema version 1.0")]
    [InlineData("""[]""", "JSON object")]
    [InlineData("""null""", "JSON object")]
    [InlineData("""{ "schema": 1, "installed": { "nexer-core": null } }""", "'nexer-core'")]
    [InlineData("""{ "schema": 1, "projects": { "github.com/acme/api": null } }""", "'github.com/acme/api'")]
    [InlineData("""{ "schema": 1, "projects": { "github.com/acme/api": {} } }""", "'github.com/acme/api'")]
    [InlineData("""{ "schema": 1, "projects": { "github.com/acme/api": { "profile": "p", "plugins": [null] } } }""", "'github.com/acme/api'")]
    [InlineData("""{ "schema": 1, "projects": { "github.com/acme/api": { "profile": "p", "mcp": [null] } } }""", "'github.com/acme/api'")]
    public void Deserialize_rejects_an_invalid_document(string json, string messageFragment)
    {
        var ex = Assert.Throws<StateFileException>(() => StateFileFormat.Deserialize(json));

        Assert.Contains(messageFragment, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "schema": 1, }""")]
    [InlineData("""{ "schema": 1 } trailing""")]
    [InlineData("""{ "schema": 1, "installed": [] }""")]
    [InlineData("""{ "schema": 1, "installed": { "nexer-core": 1 } }""")]
    [InlineData("""{ "schema": 1, "projects": { "a": { "profile": "p", "plugins": "x" } } }""")]
    [InlineData("""{ "schema": 1, "installed": { "nexer-core": "1", "nexer-core": "2" } }""")]
    [InlineData("""{ "schema": 1, "schema": 1 }""")]
    public void Deserialize_wraps_malformed_json(string json)
    {
        var ex = Assert.Throws<StateFileException>(() => StateFileFormat.Deserialize(json));

        Assert.IsType<JsonException>(ex.InnerException, exactMatch: false);
        Assert.Contains("not valid JSON", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("beta")]
    [InlineData("Nexer")]
    [InlineData("")]
    public void Deserialize_rejects_an_unknown_channel_and_keeps_the_cause(string channel)
    {
        var ex = Assert.Throws<StateFileException>(() => StateFileFormat.Deserialize($$"""{ "schema": 1, "channel": "{{channel}}" }"""));

        Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains($"'{channel}'", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Qa")]
    [InlineData("1")]
    public void Deserialize_rejects_an_unknown_role_and_keeps_the_cause(string role)
    {
        var ex = Assert.Throws<StateFileException>(() => StateFileFormat.Deserialize($$"""{ "schema": 1, "role": "{{role}}" }"""));

        Assert.IsType<ArgumentException>(ex.InnerException);
        Assert.Contains($"'{role}'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_and_deserialize_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => StateFileFormat.Serialize(null!));
        Assert.Throws<ArgumentNullException>(() => StateFileFormat.Deserialize(null!));
    }

    private static void AssertInOrder(string text, params string[] parts)
    {
        var positions = parts.Select(part => text.IndexOf(part, StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(-1, positions);
        Assert.Equal(positions.Order(), positions);
    }
}
