using NexerAI.Core.Domain;
using NexerAI.Core.Stacks;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.Stacks;

public sealed class StackSuggesterTests
{
    private static readonly StackSignal Umbraco = new("Umbraco", "nexer-dev-umbraco");

    private static readonly StackSignal Playwright = new("Playwright", null);

    private static readonly StackSignal Cypress = new("Cypress", null);

    private static readonly HashSet<string> ChannelPlugins = new(StringComparer.Ordinal) { "nexer-core", "nexer-dev-umbraco" };

    [Fact]
    public void Suggest_suggests_a_plugin_that_exists_on_the_channel()
    {
        var result = StackSuggester.Suggest([Umbraco], ChannelPlugins);

        Assert.Equal(["nexer-dev-umbraco"], result.Plugins);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Suggest_notes_a_signal_whose_plugin_is_not_on_the_channel()
    {
        var angular = new StackSignal("Angular", "nexer-dev-angular");

        var result = StackSuggester.Suggest([angular], ChannelPlugins);

        Assert.Empty(result.Plugins);
        Assert.Equal([angular], result.Notes);
    }

    [Fact]
    public void Suggest_notes_signals_without_a_plugin()
    {
        var result = StackSuggester.Suggest([Playwright, Cypress], ChannelPlugins);

        Assert.Empty(result.Plugins);
        Assert.Equal([Playwright, Cypress], result.Notes);
    }

    [Fact]
    public void Suggest_splits_mixed_signals_and_keeps_the_note_order_of_the_input()
    {
        var angular = new StackSignal("Angular", "nexer-dev-angular");
        var dotnet = new StackSignal("DotNet", "nexer-core");

        var result = StackSuggester.Suggest([Playwright, Umbraco, angular, dotnet, Cypress], ChannelPlugins);

        Assert.Equal(["nexer-core", "nexer-dev-umbraco"], result.Plugins);
        Assert.Equal([Playwright, angular, Cypress], result.Notes);
    }

    [Fact]
    public void Suggest_lists_each_plugin_and_note_once()
    {
        var umbracoVariant = new StackSignal("Umbraco 8", "nexer-dev-umbraco");

        var result = StackSuggester.Suggest([Umbraco, Playwright, umbracoVariant, Playwright, Umbraco], ChannelPlugins);

        Assert.Equal(["nexer-dev-umbraco"], result.Plugins);
        Assert.Equal([Playwright], result.Notes);
    }

    [Fact]
    public void Suggest_matches_plugin_names_with_the_comparer_of_the_channel_set()
    {
        var upperCase = new StackSignal("Umbraco", "Nexer-Dev-Umbraco");

        var result = StackSuggester.Suggest([upperCase], ChannelPlugins);

        Assert.Empty(result.Plugins);
        Assert.Equal([upperCase], result.Notes);
    }

    [Fact]
    public void Suggest_returns_nothing_for_no_signals()
    {
        var result = StackSuggester.Suggest([], ChannelPlugins);

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public void Suggest_notes_every_plugin_signal_when_the_channel_has_no_plugins()
    {
        var result = StackSuggester.Suggest([Umbraco, Playwright], new HashSet<string>());

        Assert.Empty(result.Plugins);
        Assert.Equal([Umbraco, Playwright], result.Notes);
    }

    [Fact]
    public void Suggest_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => StackSuggester.Suggest(null!, ChannelPlugins));
        Assert.Throws<ArgumentNullException>(() => StackSuggester.Suggest([], null!));
    }

    [Fact]
    public async Task SuggestAsync_detects_the_given_path_and_reads_plugins_from_the_given_channel_only()
    {
        var detector = new FakeStackDetector { Signals = [Umbraco, Playwright] };
        var source = new FakeProfileSource();
        source.PluginNames[Channel.Stable] = new HashSet<string>();
        source.PluginNames[Channel.Early] = ChannelPlugins;
        var suggester = new StackSuggester(detector, source);

        var result = await suggester.SuggestAsync(Channel.Early, @"C:\repos\website", TestContext.Current.CancellationToken);

        Assert.Equal(["nexer-dev-umbraco"], result.Plugins);
        Assert.Equal([Playwright], result.Notes);
        Assert.Equal([@"C:\repos\website"], detector.DetectedPaths);
        Assert.Equal([Channel.Early], source.RequestedChannels);
    }

    [Fact]
    public async Task SuggestAsync_rejects_null_arguments()
    {
        var detector = new FakeStackDetector();
        var source = new FakeProfileSource();
        var suggester = new StackSuggester(detector, source);
        var ct = TestContext.Current.CancellationToken;

        Assert.Throws<ArgumentNullException>(() => new StackSuggester(null!, source));
        Assert.Throws<ArgumentNullException>(() => new StackSuggester(detector, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => suggester.SuggestAsync(null!, @"C:\repos\website", ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => suggester.SuggestAsync(Channel.Stable, null!, ct));
        Assert.Empty(detector.DetectedPaths);
        Assert.Empty(source.RequestedChannels);
    }
}
