using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexerAI.Core.Doctor;

/// <summary>
/// Finds MCP server <c>env</c> and <c>headers</c> entries in Claude Code's <c>~/.claude.json</c> whose value
/// looks like a literal secret. <c>claude mcp add -e KEY=value</c> stores such values there in plain text;
/// <c>nexer-ai doctor</c> names each entry so its owner can move the secret out. Values never leave this class.
/// </summary>
public static partial class McpSecretScanner
{
    private static readonly string[] SecretKeyParts =
    [
        "TOKEN", "SECRET", "PASSWORD", "PASSWD", "APIKEY", "API_KEY", "API-KEY", "ACCESS_KEY", "PRIVATE_KEY",
        "CREDENTIAL", "AUTH",
    ];

    private static readonly string[] AuthorizationSchemes = ["Bearer ", "Basic "];

    private static readonly string[] TokenPrefixes =
    [
        "ghp_", "gho_", "ghu_", "ghs_", "github_pat_", "glpat-", "xoxb-", "xoxp-", "xoxa-", "sk-",
    ];

    /// <summary>
    /// Scans the text of <c>~/.claude.json</c>: the top-level <c>mcpServers</c> (user scope) and the
    /// <c>mcpServers</c> of each entry in <c>projects</c> (local scope). Anything without the expected shape
    /// (missing, <see langword="null"/>, arrays, non-string values) is skipped.
    /// </summary>
    /// <returns>
    /// <see cref="McpSecretScan.Scanned"/> with the findings in order: user scope first, then projects, servers,
    /// <c>env</c> before <c>headers</c>, and keys, each in document order; or <see cref="McpSecretScan.Unreadable"/>
    /// when the text is not valid JSON.
    /// </returns>
    public static McpSecretScan Scan(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            return new McpSecretScan.Scanned(FindAll(document.RootElement));
        }
        catch (JsonException)
        {
            // The exception message can quote part of the text, so it is not passed on.
            return new McpSecretScan.Unreadable();
        }
    }

    private static List<McpSecretFinding> FindAll(JsonElement root)
    {
        var findings = new List<McpSecretFinding>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            return findings;
        }

        if (root.TryGetProperty("mcpServers", out var userServers))
        {
            AddServers(findings, new McpScope.User(), userServers);
        }

        if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
        {
            foreach (var project in projects.EnumerateObject())
            {
                if (project.Value.ValueKind == JsonValueKind.Object
                    && project.Value.TryGetProperty("mcpServers", out var projectServers))
                {
                    AddServers(findings, new McpScope.Project(project.Name), projectServers);
                }
            }
        }

        return findings;
    }

    private static void AddServers(List<McpSecretFinding> findings, McpScope scope, JsonElement servers)
    {
        if (servers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var server in servers.EnumerateObject())
        {
            if (server.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AddEntries(findings, scope, server.Name, McpSecretLocation.Env, server.Value, "env");
            AddEntries(findings, scope, server.Name, McpSecretLocation.Header, server.Value, "headers");
        }
    }

    private static void AddEntries(
        List<McpSecretFinding> findings,
        McpScope scope,
        string server,
        McpSecretLocation location,
        JsonElement config,
        string propertyName)
    {
        if (!config.TryGetProperty(propertyName, out var entries) || entries.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var entry in entries.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String
                && Classify(entry.Name, entry.Value.GetString()!) is { } match)
            {
                findings.Add(new McpSecretFinding(scope, server, location, entry.Name, match));
            }
        }
    }

    /// <summary>Returns the rule that flags the entry, or <see langword="null"/> when it does not look like a secret.</summary>
    private static SecretMatch? Classify(string key, string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || EnvironmentReference().IsMatch(trimmed))
        {
            return null;
        }

        if (IsSecretKeyName(key))
        {
            return SecretMatch.KeyName;
        }

        return HasTokenShape(trimmed) ? SecretMatch.ValueShape : null;
    }

    private static bool IsSecretKeyName(string key) =>
        SecretKeyParts.Any(part => key.Contains(part, StringComparison.OrdinalIgnoreCase))
        || key.Equals("PAT", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith("_PAT", StringComparison.OrdinalIgnoreCase);

    private static bool HasTokenShape(string value) =>
        AuthorizationSchemes.Any(scheme => value.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        || TokenPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal))
        || AwsAccessKeyId().IsMatch(value)
        || JsonWebToken().IsMatch(value);

    /// <summary>
    /// A value that is only <c>${NAME}</c> or <c>${NAME:-default}</c>, optionally after a <c>Bearer</c> or
    /// <c>Basic</c> scheme: Claude Code expands it from the environment, so the file holds no secret.
    /// </summary>
    [GeneratedRegex(@"^(?:(?:Bearer|Basic) )?\$\{[A-Za-z_][A-Za-z0-9_]*(?::-[^}]*)?\}\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentReference();

    [GeneratedRegex(@"^AKIA[0-9A-Z]{16}\z", RegexOptions.CultureInvariant)]
    private static partial Regex AwsAccessKeyId();

    [GeneratedRegex(@"^eyJ[\w-]+\.eyJ[\w-]+\.[\w-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex JsonWebToken();
}
