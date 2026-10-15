using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

/// <summary>
/// An agent with state, behaving like Claude Code where the switch depends on it: user-scope plugins are shared by every
/// folder, local-scope plugins belong to the folder of the fake that holds them (<see cref="ForFolder"/>), and commands
/// whose work is already done succeed, as the adapter maps their failure codes. Every change is logged in
/// <see cref="Calls"/>, shared with the folder fakes.
/// </summary>
public sealed class FakeAgentInstaller : IAgentInstaller
{
    private readonly FakeAgentInstaller root;

    private readonly Dictionary<string, FakeAgentInstaller> folders;

    public FakeAgentInstaller()
    {
        root = this;
        folders = new Dictionary<string, FakeAgentInstaller>(StringComparer.OrdinalIgnoreCase);
        Calls = [];
        Marketplaces = [];
    }

    private FakeAgentInstaller(FakeAgentInstaller root, string folder)
    {
        this.root = root;
        folders = root.folders;
        Folder = folder;
        Calls = root.Calls;
        Marketplaces = root.Marketplaces;
    }

    /// <summary>
    /// The plugins this fake holds: on the root, the user-scope plugins and the local-scope plugins of its own folder;
    /// on a folder fake, that folder's local-scope plugins.
    /// </summary>
    public List<InstalledPlugin> Installed { get; init; } = [];

    public IReadOnlyList<InstalledMcpServer> InstalledMcpServers { get; init; } = [];

    /// <summary>The registered marketplaces; <see cref="AddMarketplaceAsync"/> adds to it.</summary>
    public List<RegisteredMarketplace> Marketplaces { get; init; }

    public List<PluginInstallRequest> InstallRequests { get; } = [];

    public List<McpServerRegistration> McpRegistrations { get; } = [];

    /// <summary>Every change, in order, as a short command-like text; a folder fake's calls end with <c> in &lt;folder&gt;</c>.</summary>
    public List<string> Calls { get; }

    /// <summary>The folder of a fake made by <see cref="ForFolder"/>; <see langword="null"/> for the root.</summary>
    public string? Folder { get; }

    /// <summary>When set, the first change whose call text starts with it throws <see cref="AgentStateException"/> before it happens.</summary>
    public string? FailOnce { get; set; }

    /// <summary>Runs when <see cref="FailOnce"/> fires, before the failure is thrown: what happens meanwhile, such as another process.</summary>
    public Action? OnFailOnce { get; set; }

    /// <summary>The fake of <paramref name="folder"/>, created with the local-scope plugins given.</summary>
    public FakeAgentInstaller WithFolder(string folder, params InstalledPlugin[] plugins)
    {
        var fake = (FakeAgentInstaller)ForFolder(folder);
        fake.Installed.AddRange(plugins);
        return fake;
    }

    public Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<InstalledPlugin>>(
            [.. root.Installed.Where(p => p.Scope == PluginScope.User), .. Installed.Where(p => p.Scope == PluginScope.Local)]);

    public Task<IReadOnlyList<InstalledMcpServer>> GetMcpServersAsync(CancellationToken ct) =>
        Task.FromResult(InstalledMcpServers);

    public Task<IReadOnlyList<RegisteredMarketplace>> GetMarketplacesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RegisteredMarketplace>>([.. Marketplaces]);

    public Task<IReadOnlyList<string>> GetPluginFoldersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
            [.. folders.Values.Where(f => f.Installed.Count > 0).Select(f => f.Folder!)]);

    public IAgentInstaller ForFolder(string folder)
    {
        if (!folders.TryGetValue(folder, out var fake))
        {
            fake = new FakeAgentInstaller(root, folder);
            folders[folder] = fake;
        }

        return fake;
    }

    public Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct)
    {
        Change($"install {request.Name}@{request.Marketplace} --scope {Scope(request.Scope)}");
        InstallRequests.Add(request);
        var owner = Owner(request.Scope);
        if (!owner.Installed.Exists(p => Is(p, request.Name, request.Marketplace, request.Scope)))
        {
            owner.Installed.Add(new InstalledPlugin(request.Name, request.Marketplace, "1.0.0", request.Scope, Enabled: true));
        }

        return Task.CompletedTask;
    }

    public Task DisablePluginAsync(string name, string marketplace, PluginScope scope, CancellationToken ct)
    {
        Change($"disable {name}@{marketplace} --scope {Scope(scope)}");
        var owner = Owner(scope);
        var index = owner.Installed.FindIndex(p => Is(p, name, marketplace, scope));
        if (index >= 0)
        {
            owner.Installed[index] = owner.Installed[index] with { Enabled = false };
        }

        return Task.CompletedTask;
    }

    public Task UninstallPluginAsync(string name, string marketplace, PluginScope scope, CancellationToken ct)
    {
        Change($"uninstall {name}@{marketplace} --scope {Scope(scope)}");
        Owner(scope).Installed.RemoveAll(p => Is(p, name, marketplace, scope));
        return Task.CompletedTask;
    }

    public Task AddMcpServerAsync(McpServerRegistration registration, CancellationToken ct)
    {
        McpRegistrations.Add(registration);
        return Task.CompletedTask;
    }

    public Task AddMarketplaceAsync(Channel channel, CancellationToken ct)
    {
        Change($"marketplace add {channel.Repository}");
        Marketplaces.Add(new RegisteredMarketplace(channel.Name, "github", channel.Repository, null));
        return Task.CompletedTask;
    }

    public Task EnableMarketplaceAutoUpdateAsync(Channel channel, CancellationToken ct)
    {
        Change($"marketplace autoUpdate {channel.Name}");
        return Task.CompletedTask;
    }

    /// <summary>Like Claude Code: removing a marketplace also uninstalls its plugins in every scope and folder.</summary>
    public Task RemoveMarketplaceAsync(string name, CancellationToken ct)
    {
        Change($"marketplace remove {name}");
        Marketplaces.RemoveAll(m => m.Name == name);
        foreach (var fake in folders.Values.Append(root))
        {
            fake.Installed.RemoveAll(p => p.Marketplace == name);
        }

        return Task.CompletedTask;
    }

    private static bool Is(InstalledPlugin plugin, string name, string marketplace, PluginScope scope) =>
        plugin.Name == name && plugin.Marketplace == marketplace && plugin.Scope == scope;

    private static string Scope(PluginScope scope) => scope == PluginScope.User ? "user" : "local";

    private FakeAgentInstaller Owner(PluginScope scope) => scope == PluginScope.User ? root : this;

    private void Change(string call)
    {
        var text = Folder is null ? call : $"{call} in {Folder}";
        if (root.FailOnce is { } prefix && text.StartsWith(prefix, StringComparison.Ordinal))
        {
            root.FailOnce = null;
            root.OnFailOnce?.Invoke();
            throw new AgentStateException($"Injected failure: {text}");
        }

        Calls.Add(text);
    }
}
