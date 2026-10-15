using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.ClaudeCode;

/// <summary>
/// Changes Claude Code's user settings file <c>settings.json</c> as text, so the adapter only reads and writes the file.
/// Claude Code rewrites the whole file when it saves a setting, and <c>claude plugin marketplace add</c> rewrites the
/// marketplace's entry without <c>autoUpdate</c> (observed on 2.1.301, undocumented), so the bootstrap writes it back
/// after every registration (design 4.1).
/// </summary>
public static class ClaudeSettings
{
    private const string FileName = "settings.json";

    private const string MarketplacesKey = "extraKnownMarketplaces";

    /// <summary>Claude Code reads this alias exactly as <see cref="MarketplacesKey"/>, which wins when both are set.</summary>
    private const string MarketplacesAlias = "additionalMarketplaces";

    private const string AutoUpdateKey = "autoUpdate";

    private static readonly JsonDocumentOptions DocumentOptions = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// As Claude Code writes the file (observed on 2.1.301): two-space indent and <c>\n</c> line ends. Characters are
    /// written as they are, not as <c>\u</c> escapes, so names with accents stay readable.
    /// </summary>
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The path of the user settings file: in <paramref name="configDirectory"/> (the <c>CLAUDE_CONFIG_DIR</c>
    /// environment variable, which replaces <c>~/.claude</c>, https://code.claude.com/docs/en/env-vars) when it is set
    /// and not empty, else in the <c>.claude</c> folder of <paramref name="userProfile"/>.
    /// </summary>
    public static string FilePath(string? configDirectory, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(userProfile);
        return Path.Combine(string.IsNullOrEmpty(configDirectory) ? Path.Combine(userProfile, ".claude") : configDirectory, FileName);
    }

    /// <summary>
    /// Returns the text of the file with <c>"autoUpdate": true</c> on the <c>extraKnownMarketplaces</c> entry of
    /// <paramref name="channel"/>, which makes Claude Code refresh the marketplace and update its plugins in the
    /// background (https://code.claude.com/docs/en/settings-reference#extraknownmarketplaces); or
    /// <see langword="null"/> when the entry already has it, so nothing needs writing. A missing entry is written whole,
    /// with a <c>github</c> source for <see cref="Channel.Repository"/>. Every other key and value is kept, in its order;
    /// the text is written again with two-space indent, <c>\n</c> line ends and a final newline.
    /// </summary>
    /// <param name="json">The text of the file, or <see langword="null"/> when it does not exist; blank text counts as an empty object.</param>
    /// <param name="channel">The channel whose marketplace entry is changed; other entries are never touched.</param>
    /// <remarks>
    /// When the file declares marketplaces only under the alias <c>additionalMarketplaces</c>, the entry is changed
    /// there: adding the canonical key would make Claude Code ignore every entry under the alias
    /// (https://code.claude.com/docs/en/settings-reference#marketplace-key-aliases).
    /// </remarks>
    /// <exception cref="AgentStateException">
    /// The text is not valid JSON (comments, trailing commas and duplicate keys included), its root is not an object, or
    /// the marketplaces object or the channel's entry is not an object. The file must then be left as it is; the
    /// message never quotes it, since it can hold secrets.
    /// </exception>
    public static string? WithMarketplaceAutoUpdate(string? json, Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var root = Parse(json);
        var key = root.ContainsKey(MarketplacesKey) || !root.ContainsKey(MarketplacesAlias) ? MarketplacesKey : MarketplacesAlias;
        if (!root.TryGetPropertyValue(key, out var marketplacesNode))
        {
            marketplacesNode = new JsonObject();
            root[key] = marketplacesNode;
        }

        if (marketplacesNode is not JsonObject marketplaces)
        {
            throw Unrecognized($"has an '{key}' that is not an object");
        }

        if (!marketplaces.TryGetPropertyValue(channel.Name, out var entryNode))
        {
            marketplaces[channel.Name] = new JsonObject
            {
                ["source"] = new JsonObject { ["source"] = "github", ["repo"] = channel.Repository },
                [AutoUpdateKey] = true,
            };
            return Write(root);
        }

        if (entryNode is not JsonObject entry)
        {
            throw Unrecognized($"has a marketplace entry '{channel.Name}' that is not an object");
        }

        if (entry[AutoUpdateKey] is JsonValue value && value.GetValueKind() == JsonValueKind.True)
        {
            return null;
        }

        entry[AutoUpdateKey] = true;
        return Write(root);
    }

    private static JsonObject Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(json, documentOptions: DocumentOptions) as JsonObject
                ?? throw Unrecognized("has a root that is not an object");
        }
        catch (JsonException ex)
        {
            // The exception message can quote part of the text, so only the line is passed on.
            throw Unrecognized($"is not valid JSON{JsonErrors.Where(ex)}");
        }
    }

    private static string Write(JsonObject root)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            root.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }

    private static AgentStateException Unrecognized(string problem) =>
        new($"Claude Code's user settings file {FileName} {problem}; it was left unchanged. Fix or remove that part of the file and try again.");
}
