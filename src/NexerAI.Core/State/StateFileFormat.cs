using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NexerAI.Core.Domain;

namespace NexerAI.Core.State;

/// <summary>
/// Reads and writes the JSON of the state file (design 4.4). The channel is stored by its marketplace name
/// (<see cref="Channel.Name"/>) and the role by its token (<see cref="RoleTokens"/>). Schema 2 added
/// <c>channelSwitch</c>; schema 1 files are still read, as schema 2 without a switch.
/// </summary>
public static class StateFileFormat
{
    /// <summary>The schema version this code writes.</summary>
    public const int SchemaVersion = 2;

    /// <summary>The oldest schema version this code still reads.</summary>
    public const int OldestReadableSchemaVersion = 1;

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// Returns the JSON text of <paramref name="state"/>: indented with two spaces, <c>\n</c> line ends
    /// and a final newline, properties in the order <c>schema</c>, <c>channel</c>, <c>role</c>,
    /// <c>installed</c>, <c>projects</c>, <c>channelSwitch</c>, dictionary keys sorted ordinally and lists in their own
    /// order. A <see langword="null"/> channel, role or switch, and the folder of a user-scope location, are left out.
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
            ChannelSwitch = state.Switch is { } pending ? ChannelSwitchDocument.Create(pending) : null,
        };
        return JsonSerializer.Serialize(document, StateFileJsonContext.Default.StateFileDocument) + "\n";
    }

    /// <summary>
    /// Parses the JSON text of a state file. Unknown properties are ignored; missing or <see langword="null"/>
    /// <c>channel</c>, <c>role</c>, <c>installed</c>, <c>projects</c>, <c>plugins</c> and <c>mcp</c> read as
    /// absent or empty, and so does a missing or <see langword="null"/> <c>channelSwitch</c> or <c>personDisabled</c>.
    /// A schema 1 file has no switch: its <c>channelSwitch</c>, if any, is ignored like any unknown property.
    /// </summary>
    /// <exception cref="StateFileException">
    /// The text is not valid JSON (duplicate property names included), its <c>schema</c> is missing or neither 1 nor
    /// <see cref="SchemaVersion"/>, or a value is invalid: unknown channel or role, a <see langword="null"/>
    /// version, project, profile or list entry, or a switch without two different known channels or with a disabled
    /// plugin that is not a <c>name@marketplace</c> id at user scope, or at local scope with its folder.
    /// </exception>
    public static InstallState Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        StateFileDocumentV1? document;
        int version;
        try
        {
            using var parsed = JsonDocument.Parse(json, DocumentOptions);
            version = CheckSchema(parsed.RootElement);

            // Schema 1 never had a switch: it is read with a type that has no such property, so a malformed one is ignored too.
            document = version == OldestReadableSchemaVersion
                ? parsed.RootElement.Deserialize(StateFileJsonContext.Default.StateFileDocumentV1)
                : parsed.RootElement.Deserialize(StateFileJsonContext.Default.StateFileDocument);
        }
        catch (JsonException ex)
        {
            throw new StateFileException($"The state file is not valid JSON: {ex.Message}", ex);
        }

        var state = ToState(document!);
        return document is StateFileDocument { ChannelSwitch: { } pending }
            ? state with { Switch = pending.ToChannelSwitch() }
            : state;
    }

    /// <summary>Returns the schema version, which must be one this code reads.</summary>
    private static int CheckSchema(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new StateFileException("The state file must contain a JSON object.");
        }

        if (!root.TryGetProperty("schema", out var schema) || schema.ValueKind == JsonValueKind.Null)
        {
            throw new StateFileException($"The state file has no 'schema' version; expected {SchemaVersion}.");
        }

        for (var version = OldestReadableSchemaVersion; version <= SchemaVersion; version++)
        {
            if (schema.ValueKind == JsonValueKind.Number && schema.GetRawText() == version.ToString(CultureInfo.InvariantCulture))
            {
                return version;
            }
        }

        throw new StateFileException(
            $"The state file has schema version {schema.GetRawText()}; this version of nexer-ai reads only schema {OldestReadableSchemaVersion} to {SchemaVersion}.");
    }

    private static Channel ParseChannel(string name, string what)
    {
        try
        {
            return Channel.Parse(name);
        }
        catch (ArgumentException ex)
        {
            throw new StateFileException($"The state file names an unknown {what} '{name}'.", ex);
        }
    }

    private static InstallState ToState(StateFileDocumentV1 document)
    {
        var channel = document.Channel is null ? null : ParseChannel(document.Channel, "channel");

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

    internal class StateFileDocumentV1
    {
        public int? Schema { get; set; }

        public string? Channel { get; set; }

        public string? Role { get; set; }

        public IDictionary<string, string?>? Installed { get; set; }

        public IDictionary<string, ProjectDocument?>? Projects { get; set; }
    }

    internal sealed class StateFileDocument : StateFileDocumentV1
    {
        // Written last, after the inherited properties.
        [JsonPropertyOrder(1)]
        public ChannelSwitchDocument? ChannelSwitch { get; set; }
    }

    internal sealed class ChannelSwitchDocument
    {
        public string? From { get; set; }

        public string? To { get; set; }

        public IList<PluginLocationDocument?>? PersonDisabled { get; set; }

        public static ChannelSwitchDocument Create(ChannelSwitch pending) => new()
        {
            From = pending.From.Name,
            To = pending.To.Name,
            PersonDisabled = [.. pending.PersonDisabled.Select(PluginLocationDocument.Create)],
        };

        public ChannelSwitch ToChannelSwitch()
        {
            if (From is null || To is null || From == To)
            {
                throw new StateFileException("The state file has a channel switch without two different channels 'from' and 'to'.");
            }

            var disabled = (PersonDisabled ?? []).Select(location => location?.ToPluginLocation()
                ?? throw new StateFileException("The state file has a channel switch with a null disabled plugin."));
            return new ChannelSwitch(ParseChannel(From, "channel"), ParseChannel(To, "channel"), Array.AsReadOnly([.. disabled]));
        }
    }

    internal sealed class PluginLocationDocument
    {
        public string? Id { get; set; }

        public string? Scope { get; set; }

        public string? Folder { get; set; }

        public static PluginLocationDocument? Create(PluginLocation location) => new()
        {
            Id = location.Id,
            Scope = location.Scope == PluginScope.User ? "user" : "local",
            Folder = location.Folder,
        };

        /// <summary>A <c>name@marketplace</c> id at <c>user</c> scope without a folder, or at <c>local</c> scope with one.</summary>
        public PluginLocation ToPluginLocation()
        {
            var at = Id?.LastIndexOf('@') ?? -1;
            PluginScope? scope = (Scope, Folder) switch
            {
                ("user", null) => PluginScope.User,
                ("local", { Length: > 0 }) => PluginScope.Local,
                _ => null,
            };
            if (at <= 0 || at == Id!.Length - 1 || scope is null)
            {
                throw new StateFileException(
                    "The state file has a channel switch with a disabled plugin that is not a name@marketplace id at user scope, or at local scope with its folder.");
            }

            return new PluginLocation(Id[..at], Id[(at + 1)..], scope.Value, Folder);
        }
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
[JsonSerializable(typeof(StateFileFormat.StateFileDocumentV1))]
internal sealed partial class StateFileJsonContext : JsonSerializerContext;
