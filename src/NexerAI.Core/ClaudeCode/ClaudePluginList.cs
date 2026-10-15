using System.Text.Json;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;
using NexerAI.Core.Tasks;

namespace NexerAI.Core.ClaudeCode;

/// <summary>
/// Reads the output of <c>claude plugin list --json</c>: an array with one object per installation, where
/// <c>id</c>, <c>version</c>, <c>scope</c>, <c>enabled</c> and <c>installPath</c> are always present
/// (https://code.claude.com/docs/en/plugins/cli-reference#json-output). Other fields are ignored.
/// </summary>
public static class ClaudePluginList
{
    private const string Command = "claude plugin list --json";

    /// <summary>
    /// Returns the user-scope plugins and the local-scope plugins of <paramref name="workingDirectory"/>, in output
    /// order. Observed on 2.1.300: the list holds the local plugins of every folder, each with its
    /// <c>projectPath</c>, and a local plugin applies only in that exact folder, not in its subfolders or its
    /// repository root. Entries at other scopes (<c>project</c>, <c>managed</c>, <c>session</c>...) and ids that are
    /// not <c>name@marketplace</c> are skipped: the bootstrap does not manage them.
    /// </summary>
    /// <param name="json">The standard output of the command.</param>
    /// <param name="workingDirectory">The absolute folder the command ran in; compared ignoring case and separators.</param>
    /// <exception cref="AgentStateException">
    /// The output is not valid JSON, not an array, or has an entry without a string <c>id</c>, <c>version</c> and
    /// <c>scope</c> and a boolean <c>enabled</c>.
    /// </exception>
    public static IReadOnlyList<InstalledPlugin> Parse(string json, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw Unrecognized("is not a JSON array");
            }

            var plugins = new List<InstalledPlugin>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (Read(entry, workingDirectory) is { } plugin)
                {
                    plugins.Add(plugin);
                }
            }

            return plugins;
        }
        catch (JsonException ex)
        {
            // The exception message can quote part of the text, so only the line is passed on.
            throw Unrecognized($"is not valid JSON (line {ex.LineNumber + 1})");
        }
    }

    private static InstalledPlugin? Read(JsonElement entry, string workingDirectory)
    {
        if (Text(entry, "id") is not { } id
            || Text(entry, "version") is not { } version
            || Text(entry, "scope") is not { } scope
            || Property(entry, "enabled") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } enabled)
        {
            throw Unrecognized("has an entry without a string id, version and scope and a boolean enabled");
        }

        PluginScope? pluginScope = scope switch
        {
            "user" => PluginScope.User,
            "local" when Text(entry, "projectPath") is { } path && WindowsPath.SameFolder(path, workingDirectory) => PluginScope.Local,
            _ => null,
        };
        var at = id.LastIndexOf('@');
        return pluginScope is { } s && at > 0 && at < id.Length - 1
            ? new InstalledPlugin(id[..at], id[(at + 1)..], version, s, enabled.GetBoolean())
            : null;
    }

    private static string? Text(JsonElement entry, string name) =>
        Property(entry, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? Property(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) ? value : null;

    private static AgentStateException Unrecognized(string problem) =>
        new($"The output of '{Command}' {problem}; this version of nexer-ai does not recognize it.");
}
