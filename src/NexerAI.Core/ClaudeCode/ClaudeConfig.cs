using System.Text.Json;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;
using NexerAI.Core.Tasks;

namespace NexerAI.Core.ClaudeCode;

/// <summary>
/// Reads Claude Code's configuration file <c>.claude.json</c> without changing it. MCP servers are read here and not
/// with <c>claude mcp list</c> or <c>get</c>, which start the servers to check their health (design 4.4,
/// https://code.claude.com/docs/en/mcp).
/// </summary>
public static class ClaudeConfig
{
    private const string FileName = ".claude.json";

    /// <summary>
    /// The path of the file: in <paramref name="configDirectory"/> (the <c>CLAUDE_CONFIG_DIR</c> environment
    /// variable) when it is set and not empty, else in <paramref name="userProfile"/>.
    /// </summary>
    public static string FilePath(string? configDirectory, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(userProfile);
        return Path.Combine(string.IsNullOrEmpty(configDirectory) ? userProfile : configDirectory, FileName);
    }

    /// <summary>
    /// Whether <paramref name="path"/> is an absolute drive path (<c>C:\x</c>) or a UNC path with a server and a share
    /// (<c>\server\share</c>), as <see cref="ProjectFolder"/> requires; either separator.
    /// </summary>
    public static bool IsAbsoluteFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return WindowsPath.Parse(path) is not null;
    }

    /// <summary>
    /// The folder whose entry in <c>projects</c> holds the local-scope MCP servers of <paramref name="workingDirectory"/>:
    /// the root of its git working copy, or the folder itself outside one, with forward slashes. Observed on 2.1.300:
    /// <c>claude mcp add --scope local</c> run in a subfolder writes under the repository root.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="workingDirectory"/> is not an absolute drive or UNC path.</exception>
    public static string ProjectFolder(IReadOnlyFileSystem fileSystem, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var start = WindowsPath.Parse(workingDirectory)
            ?? throw new ArgumentException($"The working directory '{workingDirectory}' is not absolute.", nameof(workingDirectory));
        return (GitRepository.FindRoot(fileSystem, start) ?? start).ToString();
    }

    /// <summary>
    /// Returns the MCP servers in the text of the file: user scope from the top-level <c>mcpServers</c> (observed, not
    /// documented), then local scope from <c>projects[key].mcpServers</c> of each key naming <paramref name="projectFolder"/>
    /// (documented in the scopes table of https://code.claude.com/docs/en/mcp), in document order. A server named in
    /// several keys that differ only by case or separators is returned once. Only names and scopes are kept:
    /// <c>env</c> and <c>headers</c> can hold secrets.
    /// </summary>
    /// <param name="json">The text of the file.</param>
    /// <param name="projectFolder">From <see cref="ProjectFolder"/>; keys are compared ignoring case and separators.</param>
    /// <exception cref="AgentStateException">
    /// The text is not valid JSON, or what is read has an unexpected shape: the root or <c>projects</c> is not an
    /// object, the project's entry or an <c>mcpServers</c> is neither an object nor <see langword="null"/>, or a
    /// server has neither a string <c>command</c> nor a string <c>url</c>. Other projects are not checked.
    /// </exception>
    public static IReadOnlyList<InstalledMcpServer> ReadMcpServers(string json, string projectFolder)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(projectFolder);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || (root.TryGetProperty("projects", out var projects) && projects.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null)))
            {
                throw Unrecognized("has a root or 'projects' that is not an object");
            }

            var servers = new List<InstalledMcpServer>();
            foreach (var (key, owner) in McpServerOwners(root))
            {
                if (key is null || WindowsPath.SameFolder(key, projectFolder))
                {
                    AddServers(servers, key is null ? PluginScope.User : PluginScope.Local, owner);
                }
            }

            return servers;
        }
        catch (JsonException ex)
        {
            // The exception message can quote part of the text, so only the line is passed on.
            throw Unrecognized($"is not valid JSON{JsonErrors.Where(ex)}");
        }
    }

    /// <summary>
    /// Whether the MCP server <paramref name="registration"/> names, at its scope, runs exactly its command and
    /// arguments, compared ordinally: <c>allowedMcpServers</c> entries match the command and every argument exactly
    /// (https://code.claude.com/docs/en/managed-mcp#how-servercommand-entries-match). Read like
    /// <see cref="ReadMcpServers"/>; when several keys name <paramref name="projectFolder"/>, each entry of that name
    /// must match. A missing <c>args</c> means none. <see langword="false"/> when no such server exists, when it is
    /// remote (<c>url</c>), or when its <c>command</c> or <c>args</c> has another shape. <c>env</c> is never read.
    /// </summary>
    /// <exception cref="AgentStateException">The text is not valid JSON.</exception>
    public static bool RunsCommand(string json, string projectFolder, McpServerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(projectFolder);
        ArgumentNullException.ThrowIfNull(registration);

        try
        {
            using var document = JsonDocument.Parse(json);
            var found = false;
            foreach (var (key, owner) in McpServerOwners(document.RootElement))
            {
                var scope = key is null ? PluginScope.User : PluginScope.Local;
                if (scope != registration.Scope
                    || (key is not null && !WindowsPath.SameFolder(key, projectFolder))
                    || owner.ValueKind != JsonValueKind.Object
                    || !owner.TryGetProperty("mcpServers", out var entries)
                    || entries.ValueKind != JsonValueKind.Object
                    || !entries.TryGetProperty(registration.Name, out var server))
                {
                    continue;
                }

                if (!HasCommandLine(server, registration.Command, registration.Arguments))
                {
                    return false;
                }

                found = true;
            }

            return found;
        }
        catch (JsonException ex)
        {
            throw Unrecognized($"is not valid JSON{JsonErrors.Where(ex)}");
        }
    }

    private static bool HasCommandLine(JsonElement server, string command, IReadOnlyList<string> arguments)
    {
        if (!HasString(server, "command") || server.GetProperty("command").GetString() != command)
        {
            return false;
        }

        if (!server.TryGetProperty("args", out var args))
        {
            return arguments.Count == 0;
        }

        return args.ValueKind == JsonValueKind.Array
            && args.GetArrayLength() == arguments.Count
            && args.EnumerateArray().Select((arg, i) => arg.ValueKind == JsonValueKind.String && arg.GetString() == arguments[i]).All(same => same);
    }

    /// <summary>
    /// The values that can hold an <c>mcpServers</c> object: the root (key <see langword="null"/>, user scope), then
    /// each value of <c>projects</c> with its key (local scope), in document order and whatever their kind.
    /// Nothing when the root is not an object.
    /// </summary>
    internal static IEnumerable<(string? ProjectKey, JsonElement Owner)> McpServerOwners(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        yield return (null, root);
        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
        {
            foreach (var project in projects.EnumerateObject())
            {
                yield return (project.Name, project.Value);
            }
        }
    }

    private static void AddServers(List<InstalledMcpServer> servers, PluginScope scope, JsonElement owner)
    {
        if (owner.ValueKind != JsonValueKind.Object)
        {
            throw Unrecognized("has a project entry that is not an object");
        }

        if (!owner.TryGetProperty("mcpServers", out var entries) || entries.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (entries.ValueKind != JsonValueKind.Object)
        {
            throw Unrecognized("has an 'mcpServers' that is not an object");
        }

        foreach (var server in entries.EnumerateObject())
        {
            if (!HasString(server.Value, "command") && !HasString(server.Value, "url"))
            {
                throw Unrecognized($"has an MCP server '{server.Name}' with neither a command nor a url");
            }

            if (!servers.Contains(new InstalledMcpServer(server.Name, scope)))
            {
                servers.Add(new InstalledMcpServer(server.Name, scope));
            }
        }
    }

    private static bool HasString(JsonElement server, string name) =>
        server.ValueKind == JsonValueKind.Object
        && server.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String;

    private static AgentStateException Unrecognized(string problem) =>
        new($"Claude Code's configuration file {FileName} {problem}; this version of nexer-ai does not recognize it.");
}
