using NexerAI.Core.Doctor;

namespace NexerAI.Core.Tests.Doctor;

public sealed class McpSecretScannerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("{\"mcpServers\": {\"a\": }}")]
    public void Scan_reports_text_that_is_not_json_as_unreadable(string json)
    {
        Assert.IsType<McpSecretScan.Unreadable>(McpSecretScanner.Scan(json));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"mcpServers\": null, \"projects\": null}")]
    [InlineData("{\"mcpServers\": [], \"projects\": []}")]
    [InlineData("{\"mcpServers\": {\"github\": null, \"jira\": [\"TOKEN\"]}}")]
    [InlineData("{\"mcpServers\": {\"github\": {\"env\": null, \"headers\": [\"Authorization\"]}}}")]
    [InlineData("{\"mcpServers\": {\"github\": {\"env\": {\"GITHUB_TOKEN\": 42, \"API_KEY\": null, \"SECRET\": {}}}}}")]
    [InlineData("{\"projects\": {\"C:/repos/site\": null, \"C:/repos/app\": {\"mcpServers\": \"x\"}}}")]
    public void Scan_skips_anything_that_does_not_have_the_expected_shape(string json)
    {
        Assert.Empty(Findings(json));
    }

    [Theory]
    [InlineData("GITHUB_TOKEN")]
    [InlineData("x-auth-token")]
    [InlineData("CLIENT_SECRET")]
    [InlineData("DB_PASSWORD")]
    [InlineData("SMTP_PASSWD")]
    [InlineData("OPENAPIKEY")]
    [InlineData("jira_api_key")]
    [InlineData("X-API-Key")]
    [InlineData("AWS_ACCESS_KEY_ID")]
    [InlineData("SSH_PRIVATE_KEY")]
    [InlineData("GOOGLE_APPLICATION_CREDENTIALS")]
    [InlineData("Authorization")]
    [InlineData("PAT")]
    [InlineData("pat")]
    [InlineData("AZURE_DEVOPS_PAT")]
    public void Scan_flags_a_key_name_that_suggests_a_secret(string key)
    {
        var finding = Assert.Single(Findings(UserEnv(key, "hunter2")));

        Assert.Equal(key, finding.Key);
        Assert.Equal(SecretMatch.KeyName, finding.Match);
    }

    [Theory]
    [InlineData("Bearer abc123")]
    [InlineData("bearer abc123")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("gho_abc")]
    [InlineData("ghu_abc")]
    [InlineData("ghs_abc")]
    [InlineData("github_pat_11ABCDEFG")]
    [InlineData("glpat-abcdefghij")]
    [InlineData("xoxb-123-456")]
    [InlineData("xoxp-123-456")]
    [InlineData("xoxa-123-456")]
    [InlineData("sk-abc123")]
    [InlineData("AKIAABCDEFGHIJKLMNOP")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl")]
    public void Scan_flags_a_value_with_a_known_token_shape(string value)
    {
        var finding = Assert.Single(Findings(UserEnv("UPSTREAM", value)));

        Assert.Equal("UPSTREAM", finding.Key);
        Assert.Equal(SecretMatch.ValueShape, finding.Match);
    }

    [Theory]
    [InlineData("LOG_LEVEL", "debug")]
    [InlineData("NODE_ENV", "production")]
    [InlineData("ORG_URL", "https://dev.azure.com/acme")]
    [InlineData("Accept", "application/json")]
    [InlineData("PATH_PREFIX", "/api")]
    [InlineData("REGION", "AKIA")]
    [InlineData("AWS_REGION", "akiaabcdefghijklmnop")]
    [InlineData("PAYLOAD", "eyJhbGciOiJIUzI1NiJ9")]
    public void Scan_does_not_flag_ordinary_entries(string key, string value)
    {
        Assert.Empty(Findings(UserEnv(key, value)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("${GITHUB_TOKEN}")]
    [InlineData(" ${GITHUB_TOKEN} ")]
    [InlineData("${GITHUB_TOKEN:-}")]
    [InlineData("${GITHUB_TOKEN:-fallback}")]
    [InlineData("Bearer ${GITHUB_TOKEN}")]
    [InlineData("Basic ${JIRA_BASIC_AUTH}")]
    public void Scan_skips_empty_values_and_environment_references(string value)
    {
        Assert.Empty(Findings(UserEnv("GITHUB_TOKEN", value)));
    }

    [Theory]
    [InlineData("prefix-${GITHUB_TOKEN}")]
    [InlineData("${GITHUB_TOKEN}${OTHER}")]
    [InlineData("$GITHUB_TOKEN")]
    [InlineData("Bearer abc${GITHUB_TOKEN}")]
    public void Scan_flags_values_that_are_not_only_an_environment_reference(string value)
    {
        Assert.Single(Findings(UserEnv("GITHUB_TOKEN", value)));
    }

    [Fact]
    public void Scan_prefers_the_key_name_rule_when_both_rules_match()
    {
        var finding = Assert.Single(Findings(UserEnv("GITHUB_TOKEN", "ghp_abc")));

        Assert.Equal(SecretMatch.KeyName, finding.Match);
    }

    [Fact]
    public void Scan_lists_findings_in_document_order_with_their_scope_server_and_location()
    {
        const string json = """
            {
              "projects": {
                "C:/repos/site": {
                  "mcpServers": {
                    "jira": { "type": "stdio", "command": "npx", "env": { "JIRA_API_TOKEN": "j" } }
                  }
                },
                "C:/repos/empty": { "allowedTools": [] },
                "C:/repos/app": {
                  "mcpServers": {
                    "ado": { "env": { "LOG_LEVEL": "debug", "AZURE_DEVOPS_PAT": "p" } }
                  }
                }
              },
              "mcpServers": {
                "github": {
                  "type": "http",
                  "url": "https://api.githubcopilot.com/mcp/",
                  "headers": { "X-Auth-Token": "x", "Authorization": "Bearer y" },
                  "env": { "UPSTREAM": "ghp_z", "GH_TOKEN": "${GH_TOKEN}" }
                },
                "slack": { "env": { "SLACK_BOT_TOKEN": "s" } }
              }
            }
            """;

        McpSecretFinding[] expected =
        [
            new(new McpScope.User(), "github", McpSecretLocation.Env, "UPSTREAM", SecretMatch.ValueShape),
            new(new McpScope.User(), "github", McpSecretLocation.Header, "X-Auth-Token", SecretMatch.KeyName),
            new(new McpScope.User(), "github", McpSecretLocation.Header, "Authorization", SecretMatch.KeyName),
            new(new McpScope.User(), "slack", McpSecretLocation.Env, "SLACK_BOT_TOKEN", SecretMatch.KeyName),
            new(new McpScope.Project("C:/repos/site"), "jira", McpSecretLocation.Env, "JIRA_API_TOKEN", SecretMatch.KeyName),
            new(new McpScope.Project("C:/repos/app"), "ado", McpSecretLocation.Env, "AZURE_DEVOPS_PAT", SecretMatch.KeyName),
        ];
        Assert.Equal(expected, Findings(json));
    }

    [Fact]
    public void Findings_never_print_the_secret_value()
    {
        string[] secrets =
        [
            "ghp_LeakCanary0123456789abcdefghijklmn",
            "Bearer LeakCanaryBearerValue",
            "LeakCanaryPlainPassword",
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJsZWFrIn0.TGVha0NhbmFyeQ",
        ];
        var json = $$"""
            {
              "mcpServers": {
                "github": { "env": { "UPSTREAM": "{{secrets[0]}}" }, "headers": { "Authorization": "{{secrets[1]}}" } }
              },
              "projects": {
                "C:/repos/app": {
                  "mcpServers": { "db": { "env": { "DB_PASSWORD": "{{secrets[2]}}", "SESSION": "{{secrets[3]}}" } } }
                }
              }
            }
            """;

        var scan = McpSecretScanner.Scan(json);

        var findings = Assert.IsType<McpSecretScan.Scanned>(scan).Findings;
        Assert.Equal(secrets.Length, findings.Count);
        var printed = string.Join("\n", findings.Select(f => f.ToString()).Append(scan.ToString()));
        Assert.All(secrets, secret => Assert.DoesNotContain(secret, printed, StringComparison.Ordinal));
        Assert.DoesNotContain("LeakCanary", printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJ", printed, StringComparison.Ordinal);
    }

    private static IReadOnlyList<McpSecretFinding> Findings(string json) =>
        Assert.IsType<McpSecretScan.Scanned>(McpSecretScanner.Scan(json)).Findings;

    private static string UserEnv(string key, string value) =>
        $$"""{ "mcpServers": { "server": { "env": { "{{key}}": "{{value}}" } } } }""";
}
