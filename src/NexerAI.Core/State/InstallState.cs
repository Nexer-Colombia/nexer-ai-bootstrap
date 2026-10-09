using System.Collections.ObjectModel;
using NexerAI.Core.Domain;

namespace NexerAI.Core.State;

/// <summary>
/// What the bootstrap has installed for the current user, kept in the state file.
/// It never holds secrets.
/// </summary>
/// <param name="Channel">The channel chosen by <c>nexer-ai install</c>; <see langword="null"/> before the first install.</param>
/// <param name="Role">The role chosen by <c>nexer-ai install</c>; <see langword="null"/> before the first install.</param>
/// <param name="InstalledPlugins">User-scope plugin name to installed version.</param>
/// <param name="Projects">
/// Projects set up with <c>nexer-ai project</c>, keyed by normalized remote (see
/// <see cref="Remotes.RemoteNormalizer"/>).
/// </param>
/// <remarks>Collection members compare by reference, so two states are equal only if they share dictionary instances.</remarks>
public sealed record InstallState(
    Channel? Channel,
    Role? Role,
    IReadOnlyDictionary<string, string> InstalledPlugins,
    IReadOnlyDictionary<string, ProjectState> Projects)
{
    /// <summary>The state before anything was installed.</summary>
    public static InstallState Empty { get; } = new(
        null, null, ReadOnlyDictionary<string, string>.Empty, ReadOnlyDictionary<string, ProjectState>.Empty);

    /// <summary>Returns a copy with <paramref name="project"/> added under <paramref name="remoteKey"/>, replacing any previous entry.</summary>
    /// <remarks>Keys compare ordinally, as <see cref="Remotes.RemoteNormalizer"/> keys are already canonical.</remarks>
    public InstallState WithProject(string remoteKey, ProjectState project)
    {
        ArgumentException.ThrowIfNullOrEmpty(remoteKey);
        ArgumentNullException.ThrowIfNull(project);

        var projects = new Dictionary<string, ProjectState>(Projects, StringComparer.Ordinal) { [remoteKey] = project };
        return this with { Projects = projects.AsReadOnly() };
    }
}

/// <summary>What <c>nexer-ai project</c> installed for one project.</summary>
/// <param name="Profile">Id of the project profile that matched.</param>
/// <param name="Plugins">Local-scope plugin names.</param>
/// <param name="McpServers">Local-scope MCP server names.</param>
/// <remarks>Collection members compare by reference.</remarks>
public sealed record ProjectState(string Profile, IReadOnlyList<string> Plugins, IReadOnlyList<string> McpServers);
