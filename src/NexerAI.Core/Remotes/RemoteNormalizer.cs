using System.Diagnostics.CodeAnalysis;

namespace NexerAI.Core.Remotes;

/// <summary>
/// Normalizes git remote URLs to one canonical lowercase <c>host/path</c> key, so the remotes
/// of a working copy can be matched against the <c>repos</c> of project profiles. The rules are
/// defined by the shared vectors in <c>test-vectors/remotes.json</c>, which marketplace CI also uses.
/// </summary>
public static class RemoteNormalizer
{
    private const string AzureDevOpsHost = "dev.azure.com";
    private const string VisualStudioSuffix = ".visualstudio.com";

    private static readonly string[] AcceptedSchemes = ["https", "http", "ssh", "git"];

    /// <summary>
    /// Normalizes a network remote (HTTPS, SSH URL or scp-like <c>user@host:path</c>).
    /// Returns <see langword="false"/> for empty input, local paths, <c>file://</c> URLs and anything else
    /// that is not a network remote.
    /// </summary>
    public static bool TryNormalize(string? remote, [NotNullWhen(true)] out string? key)
    {
        key = null;
        if (!TrySplit(remote?.Trim(), out var host, out var path))
        {
            return false;
        }

        host = host.ToLowerInvariant();
        var segments = Uri.UnescapeDataString(path)
            .ToLowerInvariant()
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (segments.Count > 0 && segments[^1].EndsWith(".git", StringComparison.Ordinal))
        {
            segments[^1] = segments[^1][..^".git".Length];
            segments.RemoveAll(s => s.Length == 0);
        }

        if (!IsQualifiedHost(host) || segments.Count == 0)
        {
            return false;
        }

        (host, segments) = MapAzureDevOps(host, segments);
        key = $"{host}/{string.Join('/', segments)}";
        return true;
    }

    /// <summary>Normalizes a network remote or throws <see cref="ArgumentException"/> when it is rejected.</summary>
    public static string Normalize(string remote) =>
        TryNormalize(remote, out var key)
            ? key
            : throw new ArgumentException($"'{remote}' is not a network git remote.", nameof(remote));

    /// <summary>Splits a remote into raw host and path, dropping user info, port, query and fragment.</summary>
    private static bool TrySplit(string? remote, out string host, out string path)
    {
        host = path = string.Empty;
        if (string.IsNullOrEmpty(remote) || remote.Contains('\\', StringComparison.Ordinal))
        {
            return false; // Empty, or a Windows path.
        }

        var schemeEnd = remote.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            if (!AcceptedSchemes.Contains(remote[..schemeEnd], StringComparer.OrdinalIgnoreCase))
            {
                return false; // file:// and other non-network schemes.
            }

            var rest = StripQueryAndFragment(remote[(schemeEnd + "://".Length)..]);
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            var authority = slash < 0 ? rest : rest[..slash];
            path = slash < 0 ? string.Empty : rest[slash..];
            authority = authority[(authority.LastIndexOf('@') + 1)..];
            var port = authority.IndexOf(':', StringComparison.Ordinal);
            host = port < 0 ? authority : authority[..port];
            return true;
        }

        // scp-like syntax: [user@]host:path, where the colon comes before any slash.
        var firstSlash = remote.IndexOf('/', StringComparison.Ordinal);
        var head = firstSlash < 0 ? remote : remote[..firstSlash];
        var hostStart = head.LastIndexOf('@') + 1;
        var colon = head.IndexOf(':', hostStart);
        if (colon < 0)
        {
            return false; // A local path.
        }

        host = head[hostStart..colon];
        path = StripQueryAndFragment(remote[(colon + 1)..]);
        return true;
    }

    private static string StripQueryAndFragment(string value) => value.Split('?', '#')[0];

    /// <summary>Accepts DNS names with at least one dot; this also rejects drive letters such as <c>C:</c>.</summary>
    private static bool IsQualifiedHost(string host) =>
        host.Contains('.', StringComparison.Ordinal)
        && host.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    /// <summary>Maps every Azure DevOps form to <c>dev.azure.com/org/project/_git/repo</c>.</summary>
    private static (string Host, List<string> Segments) MapAzureDevOps(string host, List<string> segments)
    {
        var isSshV3 = host is "ssh.dev.azure.com" or "vs-ssh.visualstudio.com"
            && segments.Count == 4
            && segments[0] == "v3";
        if (isSshV3)
        {
            return (AzureDevOpsHost, [segments[1], segments[2], "_git", segments[3]]);
        }

        if (host.EndsWith(VisualStudioSuffix, StringComparison.Ordinal))
        {
            var org = host[..^VisualStudioSuffix.Length];
            var rest = segments[0] == "defaultcollection" ? segments.Skip(1) : segments;
            return (AzureDevOpsHost, [org, .. rest]);
        }

        return (host, segments);
    }
}
