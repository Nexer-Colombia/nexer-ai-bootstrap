using NexerAI.Core.ClaudeCode;

namespace NexerAI.Core.Tests.ClaudeCode;

/// <summary>
/// The result lines below were recorded from <c>claude plugin install --json</c> on Claude Code 2.1.300 with an
/// isolated <c>CLAUDE_CONFIG_DIR</c> and a throwaway local marketplace <c>tmk</c>; messages are shortened.
/// </summary>
public sealed class ClaudePluginResultTests
{
    [Fact]
    public void Parse_reads_a_successful_install()
    {
        var result = ClaudePluginResult.Parse(
            """{"command":"install","outcome":"ok","plugin":"p1@tmk","pluginId":"p1@tmk","scope":"local","message":"Successfully installed plugin: p1@tmk (scope: local)","configApplied":true}""" + "\n");

        Assert.Equal(new ClaudePluginResult(Succeeded: true, FailureCode: null, ConfigApplied: true), result);
    }

    [Fact]
    public void Parse_reads_an_already_installed_plugin_as_a_success()
    {
        var result = ClaudePluginResult.Parse(
            """{"command":"install","outcome":"ok","plugin":"p1@tmk","pluginId":"p1@tmk","scope":"local","message":"Plugin \"p1@tmk\" is already installed (scope: local)","installedVersion":"1.0.0","availableVersion":"1.0.0"}""");

        Assert.Equal(new ClaudePluginResult(Succeeded: true, FailureCode: null, ConfigApplied: null), result);
    }

    [Fact]
    public void Parse_reads_a_config_value_that_was_not_applied()
    {
        // Observed on 2.1.300, not documented: an undeclared --config key still installs, with outcome ok and exit 0.
        var result = ClaudePluginResult.Parse(
            """{"command":"install","outcome":"ok","plugin":"p1@tmk","message":"Successfully installed plugin: p1@tmk (scope: user)\n⚠ Installed, but --config not applied","configApplied":false}""");

        Assert.Equal(new ClaudePluginResult(Succeeded: true, FailureCode: null, ConfigApplied: false), result);
    }

    [Fact]
    public void Parse_reads_only_the_last_line_of_a_failure()
    {
        // The documentation says a marketplace-declared command is printed ahead of the result line.
        var result = ClaudePluginResult.Parse(
            "npm install something\r\n"
            + """{"command":"install","outcome":"failed","plugin":"nope@tmk","scope":"local","message":"Plugin \"nope\" not found in marketplace \"tmk\".","failureCode":"not_found"}""" + "\r\n\r\n");

        Assert.Equal(new ClaudePluginResult(Succeeded: false, FailureCode: "not_found", ConfigApplied: null), result);
    }

    [Fact]
    public void Parse_reads_the_marketplace_a_marketplace_command_acted_on()
    {
        // Recorded on 2.1.301 (message shortened).
        var result = ClaudePluginResult.Parse(
            """{"command":"marketplace-add","outcome":"ok","marketplace":"nexer","message":"Successfully added marketplace: nexer (declared in user settings)"}""");

        Assert.Equal(new ClaudePluginResult(Succeeded: true, FailureCode: null, ConfigApplied: null, Marketplace: "nexer"), result);
    }

    [Theory]
    [InlineData("""{"command":"disable","outcome":"failed","plugin":"nexer-core@nexer","scope":"local","message":"m","failureCode":"already_in_goal_state","alreadyInGoalState":true}""", "already_in_goal_state")]
    [InlineData("""{"command":"uninstall","outcome":"failed","plugin":"nexer-core@nexer","scope":"user","message":"m","failureCode":"not_installed_at_scope","installedScope":"local"}""", "not_installed_at_scope")]
    [InlineData("""{"command":"uninstall","outcome":"failed","plugin":"nexer-core@nexer","scope":"local","message":"m","failureCode":"not_installed"}""", "not_installed")]
    [InlineData("""{"command":"marketplace-remove","marketplace":"nexer","outcome":"failed","message":"m","failureCode":"not_configured"}""", "not_configured")]
    public void Parse_reads_the_failure_codes_recorded_from_disable_uninstall_and_marketplace_remove(string line, string code)
    {
        // Recorded on 2.1.301 (messages shortened): each command run again once its work was done.
        Assert.Equal(code, ClaudePluginResult.Parse(line)!.FailureCode);
    }

    [Theory]
    [InlineData("failure code")]
    [InlineData("ghp_0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ")]
    [InlineData("")]
    public void Parse_drops_a_failure_code_that_is_not_a_short_code(string code)
    {
        var result = ClaudePluginResult.Parse($$"""{"command":"install","outcome":"failed","message":"m","failureCode":"{{code}}"}""");

        Assert.Equal(new ClaudePluginResult(Succeeded: false, FailureCode: null, ConfigApplied: null), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("Invalid scope: bad. Must be one of: user, project, local.")]
    [InlineData("""{"command":"install","outcome":"ok","message":"m"}""" + "\nSuccessfully installed")]
    [InlineData("""[{"outcome":"ok"}]""")]
    [InlineData("""{"command":"install","message":"m"}""")]
    [InlineData("""{"command":"install","outcome":true,"message":"m"}""")]
    [InlineData("""{"command":"install","outcome":"maybe","message":"m"}""")]
    [InlineData("""{"command":"install","outcome":"ok","message":"m",""")]
    public void Parse_returns_null_without_a_result_line(string output)
    {
        Assert.Null(ClaudePluginResult.Parse(output));
    }
}
