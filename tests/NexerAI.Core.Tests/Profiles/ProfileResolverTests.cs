using NexerAI.Core.Domain;
using NexerAI.Core.Profiles;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.Profiles;

public sealed class ProfileResolverTests
{
    private static readonly ProjectProfile Website = Profile("acme-website", "https://github.com/acme/website");

    private static readonly ProjectProfile Portal = Profile("acme-portal", "https://dev.azure.com/acme/portal/_git/portal");

    [Theory]
    [InlineData("https://github.com/acme/website.git")]
    [InlineData("git@github.com:Acme/website.git")]
    [InlineData("ssh://git@github.com/acme/website")]
    public void Resolve_matches_one_profile_from_any_url_form(string remote)
    {
        var result = ProfileResolver.Resolve([Website, Portal], [remote]);

        var matched = Assert.IsType<ProfileResolution.Matched>(result);
        Assert.Same(Website, matched.Profile);
        Assert.Equal(["github.com/acme/website"], matched.RemoteKeys);
    }

    [Fact]
    public void Resolve_matches_through_a_remote_other_than_origin()
    {
        // origin is a personal fork; upstream is the project repository.
        string[] remotes = ["git@github.com:someone/website.git", "https://github.com/acme/website.git"];

        var result = ProfileResolver.Resolve([Website, Portal], remotes);

        Assert.Same(Website, Assert.IsType<ProfileResolution.Matched>(result).Profile);
    }

    [Fact]
    public void Resolve_matches_a_visualstudio_com_remote_to_a_dev_azure_com_entry()
    {
        var result = ProfileResolver.Resolve([Website, Portal], ["https://acme.visualstudio.com/portal/_git/portal"]);

        var matched = Assert.IsType<ProfileResolution.Matched>(result);
        Assert.Same(Portal, matched.Profile);
        Assert.Equal(["dev.azure.com/acme/portal/_git/portal"], matched.RemoteKeys);
    }

    [Fact]
    public void Resolve_returns_unmatched_with_the_remote_keys_when_no_profile_lists_the_repository()
    {
        var result = ProfileResolver.Resolve([Website, Portal], ["https://github.com/acme/unknown.git"]);

        var unmatched = Assert.IsType<ProfileResolution.Unmatched>(result);
        Assert.Equal(["github.com/acme/unknown"], unmatched.RemoteKeys);
    }

    [Fact]
    public void Resolve_ignores_local_remotes()
    {
        string[] remotes = [@"C:\repos\website", "/srv/git/website.git", "file:///c/repos/website", ""];

        var result = ProfileResolver.Resolve([Website, Portal], remotes);

        var unmatched = Assert.IsType<ProfileResolution.Unmatched>(result);
        Assert.Empty(unmatched.RemoteKeys);
    }

    [Fact]
    public void Resolve_returns_unmatched_when_there_are_no_remotes()
    {
        var result = ProfileResolver.Resolve([Website, Portal], []);

        Assert.Empty(Assert.IsType<ProfileResolution.Unmatched>(result).RemoteKeys);
    }

    [Fact]
    public void Resolve_returns_ambiguous_profiles_ordered_by_id()
    {
        var zeta = Profile("zeta", "https://github.com/acme/website");
        var alpha = Profile("alpha", "git@github.com:acme/shared.git");
        string[] remotes = ["https://github.com/acme/website.git", "https://github.com/acme/shared.git"];

        var result = ProfileResolver.Resolve([zeta, Portal, alpha], remotes);

        var ambiguous = Assert.IsType<ProfileResolution.Ambiguous>(result);
        Assert.Equal(["alpha", "zeta"], ambiguous.Profiles.Select(p => p.Id));
        Assert.Equal(["github.com/acme/website", "github.com/acme/shared"], ambiguous.RemoteKeys);
    }

    [Fact]
    public void Resolve_counts_a_profile_matched_by_several_remotes_once()
    {
        var multi = Profile("acme-website", "https://github.com/acme/website", "https://dev.azure.com/acme/web/_git/website");
        string[] remotes = ["https://github.com/acme/website.git", "git@ssh.dev.azure.com:v3/acme/web/website"];

        var result = ProfileResolver.Resolve([multi, Portal], remotes);

        Assert.Same(multi, Assert.IsType<ProfileResolution.Matched>(result).Profile);
    }

    [Fact]
    public void Resolve_normalizes_profile_entries()
    {
        var loose = Profile("acme-website", "https://GitHub.com/Acme/Website.git", "not a remote");

        var result = ProfileResolver.Resolve([loose], ["git@github.com:acme/website.git"]);

        Assert.Same(loose, Assert.IsType<ProfileResolution.Matched>(result).Profile);
    }

    [Theory]
    [InlineData("git@github.com:acme/website.git")]
    [InlineData(" ssh://git@GitHub.com/Acme/Website.git ")]
    [InlineData("https://acme.visualstudio.com/web/_git/website")]
    [InlineData("git@ssh.dev.azure.com:v3/acme/web/website")]
    public void Resolve_matches_a_profile_entry_in_another_url_form_than_the_remote(string entry)
    {
        var profile = Profile("acme-website", entry);
        string[] remotes = ["https://github.com/acme/website.git", "https://dev.azure.com/acme/web/_git/website"];

        var result = ProfileResolver.Resolve([profile], remotes);

        Assert.Same(profile, Assert.IsType<ProfileResolution.Matched>(result).Profile);
    }

    [Theory]
    [InlineData("github.com/acme/website")]
    [InlineData("dev.azure.com/acme/web/_git/website")]
    [InlineData("acme.visualstudio.com/web/_git/website")]
    public void Resolve_ignores_profile_entries_without_a_scheme_or_scp_form(string entry)
    {
        // Profile entries are repository URLs; a bare host/path reads as a local path, as for remotes.
        string[] remotes = ["https://github.com/acme/website.git", "https://dev.azure.com/acme/web/_git/website"];

        var result = ProfileResolver.Resolve([Profile("acme-website", entry)], remotes);

        Assert.IsType<ProfileResolution.Unmatched>(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:/repos/website")]
    [InlineData(@"\\server\website")]
    [InlineData("website")]
    [InlineData("file:///c/repos/website")]
    public void Resolve_ignores_profile_entries_that_are_not_network_remotes(string entry)
    {
        var result = ProfileResolver.Resolve([Profile("acme-website", entry)], ["C:/repos/website", "https://github.com/acme/website"]);

        Assert.IsType<ProfileResolution.Unmatched>(result);
    }

    [Fact]
    public void Resolve_lists_each_remote_key_once_in_first_seen_order()
    {
        string[] remotes =
        [
            "https://github.com/acme/unknown.git",
            "git@github.com:acme/unknown.git",
            "C:/repos/unknown",
            "https://dev.azure.com/acme/web/_git/unknown",
            "https://github.com/acme/unknown",
        ];

        var result = ProfileResolver.Resolve([Website], remotes);

        Assert.Equal(["github.com/acme/unknown", "dev.azure.com/acme/web/_git/unknown"], result.RemoteKeys);
    }

    [Fact]
    public void Resolve_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => ProfileResolver.Resolve(null!, []));
        Assert.Throws<ArgumentNullException>(() => ProfileResolver.Resolve([], null!));
    }

    [Fact]
    public async Task ResolveAsync_reads_profiles_from_the_given_channel_only()
    {
        var source = new FakeProfileSource();
        source.Profiles[Channel.Stable] = [Portal];
        source.Profiles[Channel.Early] = [Website];
        var resolver = new ProfileResolver(source);

        var result = await resolver.ResolveAsync(
            Channel.Early,
            ["https://github.com/acme/website.git"],
            TestContext.Current.CancellationToken);

        Assert.Same(Website, Assert.IsType<ProfileResolution.Matched>(result).Profile);
        Assert.Equal([Channel.Early], source.RequestedChannels);
    }

    [Fact]
    public async Task ResolveAsync_rejects_null_arguments()
    {
        var resolver = new ProfileResolver(new FakeProfileSource());
        var ct = TestContext.Current.CancellationToken;

        Assert.Throws<ArgumentNullException>(() => new ProfileResolver(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => resolver.ResolveAsync(null!, [], ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => resolver.ResolveAsync(Channel.Stable, null!, ct));
    }

    private static ProjectProfile Profile(string id, params string[] repos) =>
        new(id, repos, "lead@example.com", [], [], Engram: false);
}
