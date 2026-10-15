using System.Buffers;
using System.Text.Json;

namespace NexerAI.Core.ClaudeCode;

/// <summary>
/// The result line <c>--json</c> adds to <c>claude plugin install</c>, <c>uninstall</c>, <c>enable</c>, <c>disable</c>
/// and the <c>marketplace</c> commands: one JSON object on the last line of standard output, with <c>command</c>,
/// <c>outcome</c> (<c>ok</c> or <c>failed</c>) and <c>message</c> always present and <c>failureCode</c> and
/// <c>marketplace</c> when they apply (https://code.claude.com/docs/en/plugins/cli-reference#plugin-json-result).
/// The message is never kept: it can quote configuration values.
/// </summary>
/// <param name="Succeeded">Whether <c>outcome</c> is <c>ok</c>, which includes a plugin already installed at that scope.</param>
/// <param name="FailureCode">The <c>failureCode</c>, such as <c>not_found</c>, when it is a short code; otherwise <see langword="null"/>.</param>
/// <param name="ConfigApplied">
/// The <c>configApplied</c> field, or <see langword="null"/> when absent. Observed on 2.1.300, not documented: a
/// <c>--config</c> key the plugin does not declare still installs with outcome <c>ok</c>, exit code 0 and
/// <c>configApplied</c> <see langword="false"/>.
/// </param>
/// <param name="Marketplace">The <c>marketplace</c> field of a marketplace command, the name it acted on, or <see langword="null"/> when absent.</param>
public sealed record ClaudePluginResult(bool Succeeded, string? FailureCode, bool? ConfigApplied, string? Marketplace = null)
{
    private const int MaxFailureCodeLength = 40;

    private static readonly SearchValues<char> FailureCodeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789_-");

    /// <summary>
    /// Reads the last non-empty line of <paramref name="standardOutput"/>, or returns <see langword="null"/> when it is
    /// not a result: a usage error, such as an invalid <c>--scope</c>, prints none and exits 1, and a version before
    /// 2.1.268 does not know <c>--json</c>.
    /// </summary>
    public static ClaudePluginResult? Parse(string standardOutput)
    {
        ArgumentNullException.ThrowIfNull(standardOutput);

        var text = standardOutput.TrimEnd();
        var line = text[(text.LastIndexOf('\n') + 1)..];
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("outcome", out var outcome))
            {
                return null;
            }

            bool? configApplied = root.TryGetProperty("configApplied", out var applied) && applied.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? applied.GetBoolean()
                : null;
            string? marketplace = root.TryGetProperty("marketplace", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
            return outcome.ValueKind != JsonValueKind.String
                ? null
                : outcome.GetString() switch
                {
                    "ok" => new ClaudePluginResult(true, null, configApplied, marketplace),
                    "failed" => new ClaudePluginResult(false, ReadFailureCode(root), configApplied, marketplace),
                    _ => null,
                };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadFailureCode(JsonElement root) =>
        root.TryGetProperty("failureCode", out var code)
        && code.ValueKind == JsonValueKind.String
        && code.GetString() is { Length: > 0 and <= MaxFailureCodeLength } text
        && !text.AsSpan().ContainsAnyExcept(FailureCodeCharacters)
            ? text
            : null;
}
