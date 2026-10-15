using NexerAI.Core.Domain;

namespace NexerAI.Core.Tests;

public sealed class ChannelTests
{
    [Theory]
    [InlineData("nexer", "stable")]
    [InlineData("nexer-early", "main")]
    public void Parse_maps_channel_name_to_git_ref(string name, string expectedRef)
    {
        var channel = Channel.Parse(name);

        Assert.Equal(name, channel.Name);
        Assert.Equal(expectedRef, channel.GitRef);
    }

    [Theory]
    [InlineData("nexer", "Nexer-Colombia/nexer-ai-marketplace")]
    [InlineData("nexer-early", "Nexer-Colombia/nexer-ai-marketplace-early")]
    public void Each_channel_names_its_marketplace_repository_with_the_exact_owner_casing(string name, string repository)
    {
        // strictKnownMarketplaces matches the owner case-sensitively (design 4.1), so the spelling is exact.
        Assert.Equal(repository, Channel.Parse(name).Repository);
    }

    [Fact]
    public void Parse_returns_the_shared_instances()
    {
        Assert.Same(Channel.Stable, Channel.Parse("nexer"));
        Assert.Same(Channel.Early, Channel.Parse("nexer-early"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("stable")]
    [InlineData("main")]
    [InlineData("Nexer")]
    [InlineData(" nexer")]
    public void Parse_rejects_unknown_names(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => Channel.Parse(name));

        Assert.Contains("nexer-early", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_returns_false_for_unknown_name()
    {
        Assert.False(Channel.TryParse("beta", out var channel));
        Assert.Null(channel);
    }

    [Fact]
    public void All_lists_both_channels_and_ToString_is_the_name()
    {
        Assert.Equal(["nexer", "nexer-early"], Channel.All.Select(c => c.ToString()));
    }
}
