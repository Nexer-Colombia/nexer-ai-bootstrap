using System.Diagnostics.CodeAnalysis;

namespace NexerAI.Core.Domain;

/// <summary>
/// A marketplace channel: the name registered in Claude Code, the repository whose catalog is registered under that
/// name, and the git ref of the plugins it serves. Only the two instances below exist (design 4.1).
/// </summary>
public sealed class Channel
{
    /// <summary>Default channel, follows the <c>stable</c> ref.</summary>
    public static readonly Channel Stable = new("nexer", "Nexer-Colombia/nexer-ai-marketplace", "stable");

    /// <summary>Pilot channel, follows the <c>main</c> ref.</summary>
    public static readonly Channel Early = new("nexer-early", "Nexer-Colombia/nexer-ai-marketplace-early", "main");

    /// <summary>All channels, default first.</summary>
    public static IReadOnlyList<Channel> All { get; } = [Stable, Early];

    private Channel(string name, string repository, string gitRef)
    {
        Name = name;
        Repository = repository;
        GitRef = gitRef;
    }

    /// <summary>Marketplace name as registered in Claude Code.</summary>
    public string Name { get; }

    /// <summary>
    /// The GitHub repository of the channel's catalog in <c>owner/repo</c> form, registered with
    /// <c>claude plugin marketplace add</c> without a ref or path. The owner is spelled exactly: phase 2's
    /// <c>strictKnownMarketplaces</c> compares it case-sensitively and accepts only this form
    /// (https://code.claude.com/docs/en/settings-reference#strictknownmarketplaces).
    /// </summary>
    public string Repository { get; }

    /// <summary>
    /// Git ref of the plugins the catalog points at, in <c>nexer-ai-marketplace</c>. Not part of the registration:
    /// the catalog pins it in each plugin entry.
    /// </summary>
    public string GitRef { get; }

    /// <summary>Finds a channel by its exact (case-sensitive) name.</summary>
    public static bool TryParse(string? name, [NotNullWhen(true)] out Channel? channel)
    {
        channel = All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        return channel is not null;
    }

    /// <summary>Finds a channel by name or throws <see cref="ArgumentException"/>.</summary>
    public static Channel Parse(string name) =>
        TryParse(name, out var channel)
            ? channel
            : throw new ArgumentException(
                $"Unknown channel '{name}'. Expected one of: {string.Join(", ", All.Select(c => c.Name))}.",
                nameof(name));

    /// <inheritdoc />
    public override string ToString() => Name;
}
