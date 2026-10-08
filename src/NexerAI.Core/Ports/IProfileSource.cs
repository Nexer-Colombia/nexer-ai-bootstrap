using NexerAI.Core.Domain;

namespace NexerAI.Core.Ports;

/// <summary>Reads profiles and the plugin catalog from the marketplace at a channel's ref.</summary>
public interface IProfileSource
{
    /// <summary>Returns every project profile published on the channel.</summary>
    Task<IReadOnlyList<ProjectProfile>> GetProfilesAsync(Channel channel, CancellationToken ct);

    /// <summary>Returns the names of the plugins that exist on the channel.</summary>
    Task<IReadOnlySet<string>> GetPluginNamesAsync(Channel channel, CancellationToken ct);
}
