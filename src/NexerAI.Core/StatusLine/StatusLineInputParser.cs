using System.Text.Json;

namespace NexerAI.Core.StatusLine;

/// <summary>
/// Reads the JSON object that Claude Code pipes to a status line command, field by field. A missing field,
/// a JSON <see langword="null"/> and a value of an unexpected type all read as unknown, and only that field
/// is affected; unknown fields are ignored. A status line must always print something, so this never throws
/// on its input.
/// </summary>
public static class StatusLineInputParser
{
    // DateTimeOffset.FromUnixTimeSeconds throws outside this range.
    private static readonly long MinUnixSeconds = DateTimeOffset.MinValue.ToUnixTimeSeconds();
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    /// <summary>Parses the text Claude Code wrote to standard input.</summary>
    /// <returns>
    /// The input, or <see langword="null"/> when the text is not valid JSON or its root is not an object:
    /// then nothing about the session is known.
    /// </returns>
    public static StatusLineInput? Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object ? Read(root) : null;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            // ArgumentException: text that cannot be transcoded to UTF-8, such as a lone surrogate.
            return null;
        }
    }

    private static StatusLineInput Read(JsonElement root) =>
        new(ReadPromptCache(Child(root, "prompt_cache")));

    private static PromptCacheState? ReadPromptCache(JsonElement? cache) =>
        Property(cache, "warm") is { ValueKind: JsonValueKind.True or JsonValueKind.False } warm
            ? new PromptCacheState(warm.GetBoolean(), Text(cache, "ttl"), UnixTime(cache, "expires_at"))
            : null;

    /// <summary>The named property of <paramref name="parent"/> when it is an object.</summary>
    private static JsonElement? Child(JsonElement? parent, string name) =>
        Property(parent, name, JsonValueKind.Object);

    private static string? Text(JsonElement? parent, string name) =>
        Property(parent, name, JsonValueKind.String)?.GetString();

    private static DateTimeOffset? UnixTime(JsonElement? parent, string name) =>
        Property(parent, name, JsonValueKind.Number) is { } number
        && number.TryGetInt64(out var seconds)
        && seconds >= MinUnixSeconds
        && seconds <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;

    private static JsonElement? Property(JsonElement? parent, string name, JsonValueKind kind) =>
        Property(parent, name) is { } value && value.ValueKind == kind ? value : null;

    private static JsonElement? Property(JsonElement? parent, string name) =>
        parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(name, out var property)
            ? property
            : null;
}
