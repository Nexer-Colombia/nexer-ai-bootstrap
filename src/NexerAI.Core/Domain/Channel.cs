using System.Diagnostics.CodeAnalysis;

namespace NexerAI.Core.Domain;

/// <summary>
/// A marketplace channel: the name registered in Claude Code and the git ref of the
/// marketplace repository it follows. Only the two instances below exist.
/// </summary>
public sealed class Channel
{
    /// <summary>Default channel, follows the <c>stable</c> ref.</summary>
    public static readonly Channel Stable = new("nexer", "stable");

    /// <summary>Pilot channel, follows the <c>main</c> ref.</summary>
    public static readonly Channel Early = new("nexer-early", "main");

    /// <summary>All channels, default first.</summary>
    public static IReadOnlyList<Channel> All { get; } = [Stable, Early];

    private Channel(string name, string gitRef)
    {
        Name = name;
        GitRef = gitRef;
    }

    /// <summary>Marketplace name as registered in Claude Code.</summary>
    public string Name { get; }

    /// <summary>Git ref of the marketplace repository.</summary>
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
