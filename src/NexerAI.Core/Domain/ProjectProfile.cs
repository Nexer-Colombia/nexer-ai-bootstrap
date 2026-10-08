namespace NexerAI.Core.Domain;

/// <summary>Per-project profile from the marketplace repository (design 4.3).</summary>
/// <param name="Id">Profile identifier, for example <c>acme-website</c>.</param>
/// <param name="Repos">Normalized repository remotes that select this profile.</param>
/// <param name="Owner">Tech lead who approves profile changes.</param>
/// <param name="Plugins">Stack plugins only; role plugins never appear here.</param>
/// <param name="Trackers">Issue trackers whose MCP servers the project uses.</param>
/// <param name="Engram">Whether the project adds <c>nexer-engram</c>.</param>
/// <remarks>Collection members compare by reference, so two profiles are equal only if they share list instances.</remarks>
public sealed record ProjectProfile(
    string Id,
    IReadOnlyList<string> Repos,
    string Owner,
    IReadOnlyList<string> Plugins,
    IReadOnlyList<TrackerReference> Trackers,
    bool Engram);

/// <summary>Non-secret identifiers of one tracker (<c>azure-devops</c>, <c>jira</c>, <c>testrail</c>).</summary>
public sealed record TrackerReference(string Type, Uri Url, string? Project);

/// <summary>A technology detected in a project, with the stack plugin it suggests, if any.</summary>
public sealed record StackSignal(string Name, string? SuggestedPlugin);
