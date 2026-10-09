using NexerAI.Core.Domain;
using NexerAI.Core.State;

namespace NexerAI.Core.Tests.State;

public sealed class InstallStateTests
{
    private static readonly ProjectState Website = new("acme-website", ["nexer-dev-umbraco"], ["nexer-azure-devops"]);

    private static readonly ProjectState Api = new("acme-api", [], []);

    [Fact]
    public void Empty_has_no_channel_role_plugins_or_projects()
    {
        Assert.Null(InstallState.Empty.Channel);
        Assert.Null(InstallState.Empty.Role);
        Assert.Empty(InstallState.Empty.InstalledPlugins);
        Assert.Empty(InstallState.Empty.Projects);
    }

    [Fact]
    public void WithProject_adds_a_project_and_leaves_the_original_unchanged()
    {
        var original = InstallState.Empty with { Channel = Channel.Early, Role = Role.Developer };

        var updated = original.WithProject("github.com/acme/website", Website);

        Assert.Empty(original.Projects);
        Assert.Same(Website, updated.Projects["github.com/acme/website"]);
        Assert.Same(Channel.Early, updated.Channel);
        Assert.Equal(Role.Developer, updated.Role);
    }

    [Fact]
    public void WithProject_replaces_the_project_with_the_same_key_and_keeps_the_others()
    {
        var state = InstallState.Empty
            .WithProject("github.com/acme/website", Website)
            .WithProject("github.com/acme/api", Api);

        var updated = state.WithProject("github.com/acme/website", Api);

        Assert.Equal(2, updated.Projects.Count);
        Assert.Same(Api, updated.Projects["github.com/acme/website"]);
        Assert.Same(Website, state.Projects["github.com/acme/website"]);
    }

    [Fact]
    public void WithProject_compares_keys_ordinally()
    {
        var state = InstallState.Empty
            .WithProject("github.com/acme/api", Api)
            .WithProject("github.com/acme/API", Website);

        Assert.Equal(2, state.Projects.Count);
    }

    [Fact]
    public void WithProject_rejects_null_and_empty_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => InstallState.Empty.WithProject(null!, Api));
        Assert.Throws<ArgumentException>(() => InstallState.Empty.WithProject("", Api));
        Assert.Throws<ArgumentNullException>(() => InstallState.Empty.WithProject("github.com/acme/api", null!));
    }
}
