using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.ClaudeCode;

/// <summary>
/// <c>settings-after-add-2.1.301.json</c> in <c>Samples</c> is the user <c>settings.json</c> Claude Code 2.1.301 wrote
/// in an isolated <c>CLAUDE_CONFIG_DIR</c> after <c>claude plugin marketplace add</c> of both channel repositories:
/// two-space indent, <c>\n</c> line ends, a final newline, and no <c>autoUpdate</c> (dropped by <c>add</c>).
/// </summary>
public sealed class ClaudeSettingsTests
{
    [Fact]
    public void FilePath_is_in_the_config_directory_when_set_else_in_the_dot_claude_folder()
    {
        Assert.Equal(Path.Combine(@"C:\t\cr", "settings.json"), ClaudeSettings.FilePath(@"C:\t\cr", @"C:\Users\ana"));
        Assert.Equal(Path.Combine(@"C:\Users\ana", ".claude", "settings.json"), ClaudeSettings.FilePath(null, @"C:\Users\ana"));
        Assert.Equal(Path.Combine(@"C:\Users\ana", ".claude", "settings.json"), ClaudeSettings.FilePath(string.Empty, @"C:\Users\ana"));
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_adds_it_to_the_entry_marketplace_add_wrote_and_leaves_the_other_entry_alone()
    {
        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(Sample("settings-after-add-2.1.301.json"), Channel.Stable);

        Assert.Equal(
            """
            {
              "enabledPlugins": {},
              "extraKnownMarketplaces": {
                "nexer": {
                  "source": {
                    "source": "github",
                    "repo": "Nexer-Colombia/nexer-ai-marketplace"
                  },
                  "autoUpdate": true
                },
                "nexer-early": {
                  "source": {
                    "source": "github",
                    "repo": "Nexer-Colombia/nexer-ai-marketplace-early"
                  }
                }
              }
            }

            """.ReplaceLineEndings("\n"),
            updated);
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_returns_null_when_the_entry_already_updates_itself()
    {
        var once = ClaudeSettings.WithMarketplaceAutoUpdate(Sample("settings-after-add-2.1.301.json"), Channel.Early)!;

        Assert.Null(ClaudeSettings.WithMarketplaceAutoUpdate(once, Channel.Early));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("\"true\"")]
    [InlineData("null")]
    public void WithMarketplaceAutoUpdate_replaces_any_other_value_with_true(string value)
    {
        var json = """{"extraKnownMarketplaces": {"nexer": {"source": {"source": "github", "repo": "Nexer-Colombia/nexer-ai-marketplace"}, "autoUpdate": """ + value + "}}}";

        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(json, Channel.Stable);

        Assert.Contains("\"autoUpdate\": true", updated, StringComparison.Ordinal);
        Assert.DoesNotContain(value, updated!.Replace("\"autoUpdate\": true", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_keeps_every_other_key_value_and_order_as_written()
    {
        var json = """
            {"model": "opus", "cleanupPeriodDays": 1.50, "env": {"NAME": "José Ñúñez <a&b>", "ESCAPED": "\u00e9\n"},
             "permissions": {"deny": ["Read(.env)"]}, "statusLine": {"type": "command", "command": "C:/x/nexer-ai.exe statusline"},
             "extraKnownMarketplaces": {"acme": {"source": {"source": "git", "url": "https://example.invalid/a.git"}, "autoUpdate": false},
               "nexer-early": {"source": {"source": "github", "repo": "Nexer-Colombia/nexer-ai-marketplace-early"}, "custom": [1, null, true]}},
             "unknownFutureKey": {"nested": [{"a": 1e3}]}}
            """;

        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(json, Channel.Early);

        Assert.Equal(
            """
            {
              "model": "opus",
              "cleanupPeriodDays": 1.50,
              "env": {
                "NAME": "José Ñúñez <a&b>",
                "ESCAPED": "é\n"
              },
              "permissions": {
                "deny": [
                  "Read(.env)"
                ]
              },
              "statusLine": {
                "type": "command",
                "command": "C:/x/nexer-ai.exe statusline"
              },
              "extraKnownMarketplaces": {
                "acme": {
                  "source": {
                    "source": "git",
                    "url": "https://example.invalid/a.git"
                  },
                  "autoUpdate": false
                },
                "nexer-early": {
                  "source": {
                    "source": "github",
                    "repo": "Nexer-Colombia/nexer-ai-marketplace-early"
                  },
                  "custom": [
                    1,
                    null,
                    true
                  ],
                  "autoUpdate": true
                }
              },
              "unknownFutureKey": {
                "nested": [
                  {
                    "a": 1e3
                  }
                ]
              }
            }

            """.ReplaceLineEndings("\n"),
            updated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \n")]
    [InlineData("{}")]
    public void WithMarketplaceAutoUpdate_writes_the_whole_entry_when_there_is_none(string? json)
    {
        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(json, Channel.Early);

        Assert.Equal(
            """
            {
              "extraKnownMarketplaces": {
                "nexer-early": {
                  "source": {
                    "source": "github",
                    "repo": "Nexer-Colombia/nexer-ai-marketplace-early"
                  },
                  "autoUpdate": true
                }
              }
            }

            """.ReplaceLineEndings("\n"),
            updated);
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_adds_a_missing_entry_beside_the_others()
    {
        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(
            """{"extraKnownMarketplaces": {"acme": {"source": {"source": "github", "repo": "acme/p"}}}, "theme": "dark"}""", Channel.Stable);

        Assert.Equal(
            """
            {
              "extraKnownMarketplaces": {
                "acme": {
                  "source": {
                    "source": "github",
                    "repo": "acme/p"
                  }
                },
                "nexer": {
                  "source": {
                    "source": "github",
                    "repo": "Nexer-Colombia/nexer-ai-marketplace"
                  },
                  "autoUpdate": true
                }
              },
              "theme": "dark"
            }

            """.ReplaceLineEndings("\n"),
            updated);
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_edits_the_alias_when_only_the_alias_declares_marketplaces()
    {
        // A canonical key would make Claude Code ignore every entry under the alias
        // (https://code.claude.com/docs/en/settings-reference#marketplace-key-aliases).
        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(
            """{"additionalMarketplaces": {"acme": {"source": {"source": "github", "repo": "acme/p"}}}}""", Channel.Stable);

        Assert.StartsWith("{\n  \"additionalMarketplaces\": {\n    \"acme\"", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("extraKnownMarketplaces", updated, StringComparison.Ordinal);
        Assert.Contains("\"autoUpdate\": true", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_edits_the_canonical_key_when_both_spellings_exist()
    {
        var updated = ClaudeSettings.WithMarketplaceAutoUpdate(
            """{"additionalMarketplaces": {}, "extraKnownMarketplaces": {}}""", Channel.Stable);

        Assert.StartsWith("{\n  \"additionalMarketplaces\": {},\n  \"extraKnownMarketplaces\": {\n    \"nexer\"", updated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("// comment\n{}")]
    [InlineData("{\"a\": 1,}")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"theme\": \"a\", \"theme\": \"b\"}")]
    [InlineData("{\"extraKnownMarketplaces\": {\"nexer\": {}, \"nexer\": {}}}")]
    [InlineData("{\"extraKnownMarketplaces\": []}")]
    [InlineData("{\"extraKnownMarketplaces\": null}")]
    [InlineData("{\"additionalMarketplaces\": \"x\"}")]
    [InlineData("{\"extraKnownMarketplaces\": {\"nexer\": true}}")]
    [InlineData("{\"extraKnownMarketplaces\": {\"nexer\": null}}")]
    public void WithMarketplaceAutoUpdate_refuses_text_it_cannot_change_safely_with_a_typed_error(string json)
    {
        var error = Assert.Throws<AgentStateException>(() => ClaudeSettings.WithMarketplaceAutoUpdate(json, Channel.Stable));

        Assert.Contains("settings.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithMarketplaceAutoUpdate_does_not_quote_the_file_in_the_error()
    {
        var error = Assert.Throws<AgentStateException>(
            () => ClaudeSettings.WithMarketplaceAutoUpdate("{\"env\": {\"TOKEN\": \"secret-ghp_x", Channel.Stable));

        Assert.DoesNotContain("secret", error.ToString(), StringComparison.Ordinal);
    }

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ClaudeCode", "Samples", name));
}
