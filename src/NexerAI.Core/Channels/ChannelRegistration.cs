using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Channels;

/// <summary>
/// Registers a channel's marketplace so the agent can install from it and keeps it up to date (design 4.1,
/// "Registration"). A marketplace declared only in settings is not usable from the command line until a session
/// starts (observed on 2.1.301), so the marketplace is added explicitly, and only when missing.
/// </summary>
public static class ChannelRegistration
{
    /// <summary>
    /// Adds the marketplace of <paramref name="channel"/> when <see cref="IAgentInstaller.GetMarketplacesAsync"/> does
    /// not list it, then always enables its auto-update: adding it, even again, drops the setting (observed on 2.1.301).
    /// </summary>
    /// <exception cref="AgentStateException">
    /// A marketplace of the same name is registered from another source (another repository or owner spelling, a pinned
    /// ref, a local folder): Claude Code identifies a marketplace by its name, so the channel's cannot be added beside it.
    /// Nothing is changed, and the message says how to remove it.
    /// </exception>
    public static async Task EnsureAsync(IAgentInstaller installer, Channel channel, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(channel);

        var marketplaces = await installer.GetMarketplacesAsync(ct).ConfigureAwait(false);
        var registered = marketplaces.FirstOrDefault(m => string.Equals(m.Name, channel.Name, StringComparison.Ordinal));
        if (registered is null)
        {
            await installer.AddMarketplaceAsync(channel, ct).ConfigureAwait(false);
        }
        else if (!registered.IsRegistrationOf(channel))
        {
            throw new AgentStateException(
                $"A marketplace named '{channel.Name}' is already registered from another source than {channel.Repository}. "
                + $"Remove it with 'claude plugin marketplace remove {channel.Name}', which also uninstalls its plugins, and run nexer-ai again.");
        }

        await installer.EnableMarketplaceAutoUpdateAsync(channel, ct).ConfigureAwait(false);
    }
}
