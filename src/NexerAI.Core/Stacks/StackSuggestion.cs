using NexerAI.Core.Domain;

namespace NexerAI.Core.Stacks;

/// <summary>
/// Stack plugins suggested for a working copy (design 4.4). The unknown-repository flow puts
/// <see cref="Plugins"/> in the draft profile and <see cref="Notes"/> in the profile request issue.
/// </summary>
/// <param name="Plugins">Distinct names of the suggested plugins that exist on the channel, in ordinal order.</param>
/// <param name="Notes">
/// Distinct signals that suggest no plugin on the channel, in detection order: signals without a plugin
/// (for example a Playwright configuration, relevant to the QA role) and signals whose plugin does not exist there.
/// </param>
public sealed record StackSuggestion(IReadOnlyList<string> Plugins, IReadOnlyList<StackSignal> Notes);
