namespace NexerAI.Core.StatusLine;

/// <summary>The rule for using a Claude Code <c>session_id</c> as a file name.</summary>
public static class SessionId
{
    private const int MaxLength = 128;

    private static readonly string[] DeviceNames = ["CON", "PRN", "AUX", "NUL"];

    /// <summary>
    /// Whether <paramref name="sessionId"/> is a plain token, 1 to 128 ASCII letters, digits, <c>-</c> and
    /// <c>_</c>, and not a Windows device name (<c>CON</c>, <c>COM1</c>...), so <c>{id}.json</c> names a file
    /// in the folder it is joined to and nothing else. Any other id is never used as a path.
    /// </summary>
    public static bool IsFileName(string? sessionId)
    {
        if (sessionId is not { Length: >= 1 and <= MaxLength })
        {
            return false;
        }

        foreach (var c in sessionId)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }

        return !IsDeviceName(sessionId);
    }

    private static bool IsDeviceName(string name) =>
        DeviceNames.Contains(name, StringComparer.OrdinalIgnoreCase)
        || (name.Length == 4
            && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && char.IsAsciiDigit(name[3]));
}
