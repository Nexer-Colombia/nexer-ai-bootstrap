using NexerAI.Core.Domain;
using NexerAI.Core.Ports;
using NexerAI.Core.Remotes;

namespace NexerAI.Core.Profiles;

/// <summary>
/// Finds the project profile of a working copy by matching every remote (not only <c>origin</c>)
/// against the <c>repos</c> of the profiles on the person's channel (design 4.3).
/// </summary>
public sealed class ProfileResolver(IProfileSource source)
{
    private readonly IProfileSource source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Reads the profiles published on <paramref name="channel"/> and resolves <paramref name="remotes"/> against them.</summary>
    public async Task<ProfileResolution> ResolveAsync(Channel channel, IEnumerable<string> remotes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(remotes);

        var profiles = await source.GetProfilesAsync(channel, ct).ConfigureAwait(false);
        return Resolve(profiles, remotes);
    }

    /// <summary>
    /// Matches remote URLs, as listed by <c>git remote -v</c>, against the profiles. Profile <c>repos</c>
    /// entries are repository URLs too, and both sides are normalized with <see cref="RemoteNormalizer"/>,
    /// so a bare <c>host/path</c> entry is rejected like a local path. Remotes and profile entries it
    /// rejects are ignored, and a profile matched by several remotes counts once.
    /// </summary>
    public static ProfileResolution Resolve(IReadOnlyList<ProjectProfile> profiles, IEnumerable<string> remotes)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(remotes);

        var remoteKeys = new List<string>();
        foreach (var remote in remotes)
        {
            if (RemoteNormalizer.TryNormalize(remote, out var key) && !remoteKeys.Contains(key, StringComparer.Ordinal))
            {
                remoteKeys.Add(key);
            }
        }

        var keySet = remoteKeys.ToHashSet(StringComparer.Ordinal);
        var matches = profiles
            .Where(profile => profile.Repos.Any(repo => RemoteNormalizer.TryNormalize(repo, out var key) && keySet.Contains(key)))
            .DistinctBy(profile => profile.Id, StringComparer.Ordinal)
            .OrderBy(profile => profile.Id, StringComparer.Ordinal)
            .ToList();

        return matches.Count switch
        {
            0 => new ProfileResolution.Unmatched(remoteKeys),
            1 => new ProfileResolution.Matched(matches[0], remoteKeys),
            _ => new ProfileResolution.Ambiguous(matches, remoteKeys),
        };
    }
}
