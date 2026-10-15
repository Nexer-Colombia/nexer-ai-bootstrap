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
    /// The output is not valid JSON, not an array, or has an entry without a string <c>scope</c>, or an entry that
    /// is kept without a string <c>id</c> and <c>version</c> and a boolean <c>enabled</c>. Skipped entries are not
    /// checked further, so an odd entry the bootstrap does not manage never blocks the others.
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
            throw Unrecognized($"is not valid JSON{JsonErrors.Where(ex)}");
        }
    }

    /// <summary>
    /// Returns the <c>projectPath</c> of every local-scope entry, each folder once (compared ignoring case and
    /// separators, the first spelling kept), in output order. Entries without an absolute <c>projectPath</c> are
    /// skipped. Only the folders are read: <c>enabled</c> is the state in the folder the command ran in, not in the
    /// entry's own folder (observed on 2.1.301), so each folder's plugins are listed from that folder.
    /// </summary>
    /// <param name="json">The standard output of the command.</param>
    /// <exception cref="AgentStateException">The output is not valid JSON, not an array, or has an entry without a string <c>scope</c>.</exception>
    public static IReadOnlyList<string> LocalFolders(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw Unrecognized("is not a JSON array");
            }

            var folders = new List<string>();
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                var scope = Text(entry, "scope") ?? throw Unrecognized("has an entry without a string scope");
                if (scope == "local"
                    && Text(entry, "projectPath") is { } path
                    && WindowsPath.Parse(path) is not null
                    && !folders.Exists(folder => WindowsPath.SameFolder(folder, path)))
                {
                    folders.Add(path);
                }
            }

            return folders;
        }
        catch (JsonException ex)
        {
            throw Unrecognized($"is not valid JSON{JsonErrors.Where(ex)}");
        }
    }

    private static InstalledPlugin? Read(JsonElement entry, string workingDirectory)
    {
        PluginScope? pluginScope = Text(entry, "scope") switch
        {
            null => throw Unrecognized("has an entry without a string scope"),
            "user" => PluginScope.User,
            "local" when Text(entry, "projectPath") is { } path && WindowsPath.SameFolder(path, workingDirectory) => PluginScope.Local,
            _ => null,
        };
        if (pluginScope is not { } scope)
        {
            return null;
        }

        if (Text(entry, "id") is not { } id
            || Text(entry, "version") is not { } version
            || Property(entry, "enabled") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } enabled)
        {
            throw Unrecognized("has an entry without a string id and version and a boolean enabled");
        }

        var at = id.LastIndexOf('@');
        return at > 0 && at < id.Length - 1
            ? new InstalledPlugin(id[..at], id[(at + 1)..], version, scope, enabled.GetBoolean())
            : null;
    }

    private static string? Text(JsonElement entry, string name) =>
        Property(entry, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static JsonElement? Property(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) ? value : null;

    private static AgentStateException Unrecognized(string problem) =>
        new($"The output of '{Command}' {problem}; this version of nexer-ai does not recognize it.");
}
