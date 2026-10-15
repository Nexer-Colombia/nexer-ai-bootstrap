using System.Text.Json;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.ClaudeCode;

/// <summary>
/// Reads the output of <c>claude plugin marketplace list --json</c>: an array with one object per marketplace, every
/// field a string, with <c>name</c>, <c>source</c>, <c>repo</c> (<c>github</c> only) and <c>ref</c> (only when pinned)
/// (https://code.claude.com/docs/en/plugins/cli-reference#plugin-marketplace-list). Other fields are ignored.
/// </summary>
public static class ClaudeMarketplaceList
{
    private const string Command = "claude plugin marketplace list --json";

    /// <summary>Returns the marketplaces in output order.</summary>
    /// <param name="json">The standard output of the command.</param>
    /// <exception cref="AgentStateException">
    /// The output is not valid JSON, not an array, or has an entry without a string <c>name</c> and <c>source</c>, or
    /// with a <c>repo</c> or <c>ref</c> that is not a string.
    /// </exception>
    public static IReadOnlyList<RegisteredMarketplace> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw Unrecognized("is not a JSON array");
            }

            return [.. document.RootElement.EnumerateArray().Select(Read)];
        }
        catch (JsonException ex)
        {
            // The exception message can quote part of the text, so only the line is passed on.
            throw Unrecognized($"is not valid JSON{JsonErrors.Where(ex)}");
        }
    }

    private static RegisteredMarketplace Read(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || Text(entry, "name") is not { } name
            || Text(entry, "source") is not { } source)
        {
            throw Unrecognized("has an entry without a string name and source");
        }

        return new RegisteredMarketplace(name, source, OptionalText(entry, "repo"), OptionalText(entry, "ref"));
    }

    private static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? OptionalText(JsonElement entry, string name) =>
        !entry.TryGetProperty(name, out var value)
            ? null
            : value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw Unrecognized($"has an entry whose '{name}' is not a string");

    private static AgentStateException Unrecognized(string problem) =>
        new($"The output of '{Command}' {problem}; this version of nexer-ai does not recognize it.");
}
