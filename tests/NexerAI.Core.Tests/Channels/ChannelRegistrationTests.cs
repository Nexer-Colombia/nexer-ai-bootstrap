using NexerAI.Core.Channels;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.Channels;

public sealed class ChannelRegistrationTests
{
    private static readonly RegisteredMarketplace StableRegistration = new("nexer", "github", "Nexer-Colombia/nexer-ai-marketplace", null);

    [Fact]
    public async Task EnsureAsync_adds_a_missing_marketplace_and_then_sets_its_auto_update()
    {
        var installer = new FakeAgentInstaller { Marketplaces = [StableRegistration] };

        await ChannelRegistration.EnsureAsync(installer, Channel.Early, TestContext.Current.CancellationToken);

        Assert.Equal(["marketplace add Nexer-Colombia/nexer-ai-marketplace-early", "marketplace autoUpdate nexer-early"], installer.Calls);
    }

    [Fact]
    public async Task EnsureAsync_never_adds_a_registered_marketplace_again_but_still_sets_its_auto_update()
    {
        // marketplace add, even a repeat one, drops autoUpdate from the entry (observed on 2.1.301), so it is set every run.
        var installer = new FakeAgentInstaller { Marketplaces = [StableRegistration] };

        await ChannelRegistration.EnsureAsync(installer, Channel.Stable, TestContext.Current.CancellationToken);

        Assert.Equal(["marketplace autoUpdate nexer"], installer.Calls);
    }

    [Theory]
    [InlineData("github", "Nexer-Colombia/nexer-ai-marketplace", "stable")]
    [InlineData("github", "nexer-colombia/nexer-ai-marketplace", null)]
    [InlineData("github", "someone/fork", null)]
    [InlineData("directory", null, null)]
    public async Task EnsureAsync_refuses_a_marketplace_of_the_same_name_from_another_source(string source, string? repository, string? gitRef)
    {
        var installer = new FakeAgentInstaller { Marketplaces = [new RegisteredMarketplace("nexer", source, repository, gitRef)] };

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => ChannelRegistration.EnsureAsync(installer, Channel.Stable, TestContext.Current.CancellationToken));

        Assert.Contains("claude plugin marketplace remove nexer", error.Message, StringComparison.Ordinal);
        Assert.Empty(installer.Calls);
    }
}
