using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexerAI.Core.Domain;

namespace NexerAI.Core.State;

/// <summary>
/// Reads and writes the JSON of the state file, schema version 1 (design 4.4). The channel is stored
/// by its marketplace name (<see cref="Channel.Name"/>) and the role by its token (<see cref="RoleTokens"/>).
/// </summary>
public static class StateFileFormat
{
    /// <summary>The only schema version this code reads and writes.</summary>
    public const int SchemaVersion = 1;

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// Returns the JSON text of <paramref name="state"/>: indented with two spaces, <c>\n</c> line ends
    /// and a final newline, properties in the order <c>schema</c>, <c>channel</c>, <c>role</c>,
    /// <c>installed</c>, <c>projects</c>, dictionary keys sorted ordinally and lists in their own order.
    /// A <see langword="null"/> channel or role is left out.
    /// </summary>
    public static string Serialize(InstallState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var installed = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, version) in state.InstalledPlugins)
        {
            installed[name] = version;
        }

        var projects = new SortedDictionary<string, ProjectDocument?>(StringComparer.Ordinal);
        foreach (var (key, project) in state.Projects)
        {
            projects[key] = ProjectDocument.From(project);
        }

        var document = new StateFileDocument
        {
            Schema = SchemaVersion,
            Channel = state.Channel?.Name,
            Role = state.Role?.ToToken(),
            Installed = installed,
            Projects = projects,
        };
        return JsonSerializer.Serialize(document, StateFileJsonContext.Default.StateFileDocument) + "\n";
    }

    /// <summary>
    /// Parses the JSON text of a state file. Unknown properties are ignored; missing or <see langword="null"/>
    /// <c>channel</c>, <c>role</c>, <c>installed</c>, <c>projects</c>, <c>plugins</c> and <c>mcp</c> read as
    /// absent or empty.
    /// </summary>
    /// <exception cref="StateFileException">
    /// The text is not valid JSON (duplicate property names included), its <c>schema</c> is missing or not
    /// <see cref="SchemaVersion"/>, or a value is invalid: unknown channel or role, a <see langword="null"/>
    /// version, project, profile or list entry.
    /// </exception>
    public static InstallState Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        StateFileDocument? document;
        try
        {
            using var parsed = JsonDocument.Parse(json, DocumentOptions);
            CheckSchema(parsed.RootElement);
            document = parsed.RootElement.Deserialize(StateFileJsonContext.Default.StateFileDocument);
        }
        catch (JsonException ex)
        {
            throw new StateFileException($"The state file is not valid JSON: {ex.Message}", ex);
        }

        return ToState(document!);
    }

    private static void CheckSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new StateFileException("The state file must contain a JSON object.");
        }

        if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind == JsonValueKind.Null)
        {
            throw new StateFileException($"The state file has no 'schema' version; expected {SchemaVersion}.");
        }

        if (schema.ValueKind != JsonValueKind.Number || schema.GetRawText() != SchemaVersion.ToString(CultureInfo.InvariantCulture))
        {
            throw new StateFileException(
                $"The state file has schema version {schema.GetRawText()}; this version of nexer-ai reads only schema {SchemaVersion}.");
        }
    }

    private static InstallState ToState(StateFileDocument document)
    {
        Channel? channel = null;
        if (document.Channel is not null)
        {
            try
            {
                channel = Channel.Parse(document.Channel);
            }
            catch (ArgumentException ex)
            {
                throw new StateFileException($"The state file names an unknown channel '{document.Channel}'.", ex);
            }
        }

        Role? role = null;
        if (document.Role is not null)
        {
            try
            {
                role = RoleTokens.Parse(document.Role);
            }
            catch (ArgumentException ex)
            {
                throw new StateFileException($"The state file names an unknown role '{document.Role}'.", ex);
            }
        }

        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, version) in document.Installed ?? new Dictionary<string, string?>())
        {
            installed[name] = version ?? throw new StateFileException($"The state file has no version for installed plugin '{name}'.");
        }

        var projects = new Dictionary<string, ProjectState>(StringComparer.Ordinal);
        foreach (var (key, project) in document.Projects ?? new Dictionary<string, ProjectDocument?>())
        {
            projects[key] = project?.ToProjectState(key) ?? throw new StateFileException($"Project '{key}' in the state file is null.");
        }

        return new InstallState(channel, role, installed.AsReadOnly(), projects.AsReadOnly());
    }

    internal sealed class StateFileDocument
    {
        public int? Schema { get; set; }

        public string? Channel { get; set; }

        public string? Role { get; set; }

        public IDictionary<string, string?>? Installed { get; set; }

        public IDictionary<string, ProjectDocument?>? Projects { get; set; }
    }

    internal sealed class ProjectDocument
    {
        public string? Profile { get; set; }

        public IList<string?>? Plugins { get; set; }

        public IList<string?>? Mcp { get; set; }

        public static ProjectDocument From(ProjectState project) => new()
        {
            Profile = project.Profile,
            Plugins = [.. project.Plugins],
            Mcp = [.. project.McpServers],
        };

        public ProjectState ToProjectState(string key)
        {
            if (Profile is null)
            {
                throw new StateFileException($"Project '{key}' in the state file has no profile.");
            }

            return new ProjectState(Profile, Names(Plugins, key, "plugin"), Names(Mcp, key, "MCP server"));
        }

        private static ReadOnlyCollection<string> Names(IList<string?>? names, string key, string kind) =>
            Array.AsReadOnly([.. (names ?? []).Select(name => name ?? throw new StateFileException($"Project '{key}' in the state file has a null {kind} name."))]);
    }
}

/// <summary>Source-generated serializer for <see cref="StateFileFormat"/>, so Native AOT needs no reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    NewLine = "\n",
    AllowDuplicateProperties = false)]
[JsonSerializable(typeof(StateFileFormat.StateFileDocument))]
internal sealed partial class StateFileJsonContext : JsonSerializerContext;
