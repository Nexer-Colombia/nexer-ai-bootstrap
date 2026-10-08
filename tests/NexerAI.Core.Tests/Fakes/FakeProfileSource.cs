using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeProfileSource : IProfileSource
{
    public Dictionary<Channel, IReadOnlyList<ProjectProfile>> Profiles { get; } = [];

    public Dictionary<Channel, IReadOnlySet<string>> PluginNames { get; } = [];

    public List<Channel> RequestedChannels { get; } = [];

    public Task<IReadOnlyList<ProjectProfile>> GetProfilesAsync(Channel channel, CancellationToken ct)
    {
        RequestedChannels.Add(channel);
        return Task.FromResult(Profiles.GetValueOrDefault(channel) ?? []);
    }

    public Task<IReadOnlySet<string>> GetPluginNamesAsync(Channel channel, CancellationToken ct)
    {
        RequestedChannels.Add(channel);
        return Task.FromResult(PluginNames.GetValueOrDefault(channel) ?? new HashSet<string>());
    }
}
