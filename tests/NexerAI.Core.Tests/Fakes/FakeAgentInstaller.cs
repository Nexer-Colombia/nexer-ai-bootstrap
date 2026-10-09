using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

public sealed class FakeAgentInstaller : IAgentInstaller
{
    public IReadOnlyList<InstalledPlugin> Installed { get; init; } = [];

    public IReadOnlyList<InstalledMcpServer> InstalledMcpServers { get; init; } = [];

    public List<PluginInstallRequest> InstallRequests { get; } = [];

    public List<McpServerRegistration> McpRegistrations { get; } = [];

    public Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct) =>
        Task.FromResult(Installed);

    public Task<IReadOnlyList<InstalledMcpServer>> GetMcpServersAsync(CancellationToken ct) =>
        Task.FromResult(InstalledMcpServers);

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
}
