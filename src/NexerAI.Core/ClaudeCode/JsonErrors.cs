using System.Text.Json;

namespace NexerAI.Core.ClaudeCode;

/// <summary>Describes where JSON text is invalid without quoting it: the exception message can quote part of the text.</summary>
internal static class JsonErrors
{
    /// <summary>
    /// <c> (line N)</c>, one-based, or an empty string when <paramref name="error"/> has no line:
    /// <see cref="JsonException.LineNumber"/> is nullable.
    /// </summary>
    public static string Where(JsonException error) =>
        error.LineNumber is { } line ? $" (line {line + 1})" : string.Empty;
}
