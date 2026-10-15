using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeAgentInstaller : IAgentInstaller
{
    public IReadOnlyList<InstalledPlugin> Installed { get; init; } = [];

    public IReadOnlyList<InstalledMcpServer> InstalledMcpServers { get; init; } = [];

    /// <summary>The registered marketplaces; <see cref="AddMarketplaceAsync"/> adds to it.</summary>
    public List<RegisteredMarketplace> Marketplaces { get; init; } = [];

    public List<PluginInstallRequest> InstallRequests { get; } = [];

    public List<McpServerRegistration> McpRegistrations { get; } = [];

    /// <summary>Every marketplace change, in order, as a short command-like text.</summary>
    public List<string> Calls { get; } = [];

    public Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct) =>
        Task.FromResult(Installed);

    public Task<IReadOnlyList<InstalledMcpServer>> GetMcpServersAsync(CancellationToken ct) =>
        Task.FromResult(InstalledMcpServers);

    public Task<IReadOnlyList<RegisteredMarketplace>> GetMarketplacesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RegisteredMarketplace>>([.. Marketplaces]);

    public Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct)
    {
        InstallRequests.Add(request);
        return Task.CompletedTask;
    }

    public Task AddMcpServerAsync(McpServerRegistration registration, CancellationToken ct)
    {
        McpRegistrations.Add(registration);
        return Task.CompletedTask;
    }

    public Task AddMarketplaceAsync(Channel channel, CancellationToken ct)
    {
        Calls.Add($"marketplace add {channel.Repository}");
        Marketplaces.Add(new RegisteredMarketplace(channel.Name, "github", channel.Repository, null));
        return Task.CompletedTask;
    }

    public Task EnableMarketplaceAutoUpdateAsync(Channel channel, CancellationToken ct)
    {
        Calls.Add($"marketplace autoUpdate {channel.Name}");
        return Task.CompletedTask;
    }
}
