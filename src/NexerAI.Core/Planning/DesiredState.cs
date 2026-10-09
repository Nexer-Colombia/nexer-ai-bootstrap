using System.Collections.ObjectModel;
using NexerAI.Core.Domain;

namespace NexerAI.Core.Planning;

/// <summary>
/// The plugins and MCP servers one command wants present (design 4.4): <see cref="ForMachine"/> for
/// <c>nexer-ai install</c> and <see cref="ForProject"/> for <c>nexer-ai project</c>.
/// <see cref="InstallPlanner"/> turns it into actions.
/// </summary>
/// <param name="Plugins">Plugins to have installed; configuration values are empty for now.</param>
/// <param name="McpServers">MCP servers to have registered.</param>
/// <remarks>
/// Listing only what must be present, it cannot express removals: dropping plugins or servers that
/// are no longer wanted needs the state file. Switching channel, credentials, plugin configuration
/// values and drift in an MCP server's command or arguments are out of its scope too.
/// </remarks>
public sealed record DesiredState(IReadOnlyList<PluginInstallRequest> Plugins, IReadOnlyList<McpServerRegistration> McpServers)
{
    /// <summary>The plugin every person installs, at user scope.</summary>
    public const string CorePlugin = "nexer-core";

    private static readonly IReadOnlyDictionary<string, string> NoConfig = ReadOnlyDictionary<string, string>.Empty;

    /// <summary>
    /// User-scope plugins from <paramref name="channel"/>: <c>nexer-core</c>, then <c>nexer-qa</c> (QA)
    /// or <c>nexer-po</c> (PM/PO), then <c>nexer-mod</c> when <paramref name="includeMod"/> is set.
    /// </summary>
    public static DesiredState ForMachine(Role role, Channel channel, bool includeMod)
    {
        ArgumentNullException.ThrowIfNull(channel);

        string? rolePlugin = role switch
        {
            Role.Developer => null,
            Role.Qa => "nexer-qa",
            Role.ProductOwner => "nexer-po",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Undefined role."),
        };
        string?[] names = [CorePlugin, rolePlugin, includeMod ? "nexer-mod" : null];
        var plugins = names.OfType<string>()
            .Select(name => new PluginInstallRequest(name, channel.Name, PluginScope.User, NoConfig))
            .ToList();
        return new DesiredState(plugins, []);
    }

    /// <summary>
    /// Local-scope plugins from <paramref name="channel"/> (the profile's stack plugins, plus
    /// <c>nexer-engram</c> when the profile uses it) and one local-scope MCP server per tracker,
    /// each listed once and ordered by name.
    /// </summary>
    /// <param name="profile">The project profile.</param>
    /// <param name="channel">The person's channel; its name is the plugins' marketplace.</param>
    /// <param name="bootstrapExecutable">Absolute path of <c>nexer-ai.exe</c>, so the servers do not depend on <c>PATH</c>.</param>
    /// <remarks>
    /// A tracker of type <c>t</c> registers <c>nexer-t</c> as
    /// <c>&lt;bootstrapExecutable&gt; mcp-launch --type t --url &lt;url&gt;</c>. The URL is passed as the
    /// profile wrote it (<see cref="Uri.OriginalString"/>): <see cref="Uri.ToString"/> unescapes characters
    /// such as <c>%20</c>, and the launcher parses the text back into the same <see cref="Uri"/> to find the
    /// credential. Two trackers of one type with different URLs would need the same server name, so they
    /// are rejected.
    /// </remarks>
    public static DesiredState ForProject(ProjectProfile profile, Channel channel, string bootstrapExecutable)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(bootstrapExecutable);
        if (!Path.IsPathFullyQualified(bootstrapExecutable))
        {
            throw new ArgumentException($"The bootstrap executable path '{bootstrapExecutable}' is not absolute.", nameof(bootstrapExecutable));
        }

        var names = new SortedSet<string>(profile.Plugins, StringComparer.Ordinal);
        if (profile.Engram)
        {
            names.Add("nexer-engram");
        }

        var trackers = new SortedDictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var tracker in profile.Trackers)
        {
            if (!tracker.Url.IsAbsoluteUri)
            {
                throw new ArgumentException($"Tracker '{tracker.Type}' has a relative URL '{tracker.Url}'.", nameof(profile));
            }

            if (trackers.TryGetValue(tracker.Type, out var url) && url != tracker.Url)
            {
                throw new ArgumentException($"Profile '{profile.Id}' lists tracker type '{tracker.Type}' with two URLs.", nameof(profile));
            }

            trackers.TryAdd(tracker.Type, tracker.Url);
        }

        var plugins = names.Select(name => new PluginInstallRequest(name, channel.Name, PluginScope.Local, NoConfig)).ToList();
        var servers = trackers
            .Select(t => new McpServerRegistration(
                $"nexer-{t.Key}", PluginScope.Local, bootstrapExecutable, ["mcp-launch", "--type", t.Key, "--url", t.Value.OriginalString]))
            .ToList();
        return new DesiredState(plugins, servers);
    }
}
