using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Stacks;

/// <summary>
/// Turns the technologies detected in a working copy into stack plugin suggestions, limited to the
/// plugins that exist on the person's channel (design 4.4).
/// </summary>
public sealed class StackSuggester(IStackDetector detector, IProfileSource source)
{
    private readonly IStackDetector detector = detector ?? throw new ArgumentNullException(nameof(detector));

    private readonly IProfileSource source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Detects the signals under <paramref name="projectPath"/>, reads the plugin names published on
    /// <paramref name="channel"/> and splits the signals with <see cref="Suggest"/>.
    /// </summary>
    public async Task<StackSuggestion> SuggestAsync(Channel channel, string projectPath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(projectPath);

        var signals = detector.Detect(projectPath);
        var pluginNames = await source.GetPluginNamesAsync(channel, ct).ConfigureAwait(false);
        return Suggest(signals, pluginNames);
    }

    /// <summary>
    /// Suggests the plugin of every signal whose plugin is in <paramref name="channelPlugins"/> (compared
    /// with that set's comparer) and keeps every other signal as a note. Repeated plugins and repeated
    /// signals count once.
    /// </summary>
    public static StackSuggestion Suggest(IEnumerable<StackSignal> signals, IReadOnlySet<string> channelPlugins)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(channelPlugins);

        var plugins = new SortedSet<string>(StringComparer.Ordinal);
        var notes = new List<StackSignal>();
        foreach (var signal in signals)
        {
            if (signal.SuggestedPlugin is { } plugin && channelPlugins.Contains(plugin))
            {
                plugins.Add(plugin);
            }
            else if (!notes.Contains(signal))
            {
                notes.Add(signal);
            }
        }

        return new StackSuggestion([.. plugins], notes);
    }
}
