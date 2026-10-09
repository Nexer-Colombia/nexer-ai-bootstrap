using NexerAI.Core.Domain;

namespace NexerAI.Core.Profiles;

/// <summary>
/// Outcome of matching a working copy's remotes against the project profiles (design 4.3).
/// The cases below are the only ones: exactly one profile, none, or several.
/// </summary>
public abstract record ProfileResolution
{
    private ProfileResolution(IReadOnlyList<string> remoteKeys) => RemoteKeys = remoteKeys;

    /// <summary>
    /// Distinct normalized keys of the repository's network remotes, in first-seen order.
    /// The unknown-repository flow puts them in the profile request issue.
    /// </summary>
    public IReadOnlyList<string> RemoteKeys { get; }

    /// <summary>Exactly one profile lists one of the remotes.</summary>
    public sealed record Matched(ProjectProfile Profile, IReadOnlyList<string> RemoteKeys)
        : ProfileResolution(RemoteKeys);

    /// <summary>No profile lists any of the remotes: the unknown-repository flow applies.</summary>
    public sealed record Unmatched(IReadOnlyList<string> RemoteKeys)
        : ProfileResolution(RemoteKeys);

    /// <summary>Several profiles list the remotes; <paramref name="Profiles"/> is ordered by <c>Id</c>.</summary>
    public sealed record Ambiguous(IReadOnlyList<ProjectProfile> Profiles, IReadOnlyList<string> RemoteKeys)
        : ProfileResolution(RemoteKeys);
}
