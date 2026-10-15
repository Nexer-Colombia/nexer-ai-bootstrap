using System.ComponentModel;
using NexerAI.Adapters.Processes;
using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.ClaudeCode;

/// <summary>
/// The Claude Code adapter of <see cref="IAgentInstaller"/> for one working directory (design 4.4). Plugins come from
/// <c>claude plugin list --json</c>; MCP servers from <c>.claude.json</c>, read without starting them. Installing
/// and registering come in the next work unit.
/// </summary>
public sealed class ClaudeCodeAgentInstaller : IAgentInstaller
{
    private const string Claude = "claude";

    private readonly IProcessRunner processRunner;

    private readonly IReadOnlyFileSystem fileSystem;

    private readonly string configFile;

    private readonly string workingDirectory;

    /// <summary>Creates the adapter.</summary>
    /// <param name="processRunner">Runs <c>claude</c>.</param>
    /// <param name="fileSystem">Reads <paramref name="configFile"/> and the git files that locate the repository root.</param>
    /// <param name="configFile">Absolute path of <c>.claude.json</c>, usually <see cref="DefaultConfigFile"/>.</param>
    /// <param name="workingDirectory">Absolute folder the commands act on.</param>
    public ClaudeCodeAgentInstaller(IProcessRunner processRunner, IReadOnlyFileSystem fileSystem, string configFile, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(configFile);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (!Path.IsPathFullyQualified(configFile))
        {
            throw new ArgumentException($"The Claude Code configuration file '{configFile}' is not absolute.", nameof(configFile));
        }

        if (!Path.IsPathFullyQualified(workingDirectory))
        {
            throw new ArgumentException($"The working directory '{workingDirectory}' is not absolute.", nameof(workingDirectory));
        }

        this.processRunner = processRunner;
        this.fileSystem = fileSystem;
        this.configFile = configFile;
        this.workingDirectory = workingDirectory;
    }

    /// <summary>The <c>.claude.json</c> of the current user, honoring <c>CLAUDE_CONFIG_DIR</c> (see <see cref="ClaudeConfig.FilePath"/>).</summary>
    public static string DefaultConfigFile => ClaudeConfig.FilePath(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <inheritdoc />
    /// <exception cref="AgentStateException"><c>claude</c> cannot be started, fails, or prints output that is not recognized.</exception>
    public async Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct)
    {
        ProcessResult result;
        try
        {
            result = await processRunner.RunAsync(Claude, ["plugin", "list", "--json"], workingDirectory, ct).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            throw new AgentStateException("Claude Code cannot be started: 'claude' was not found on PATH or failed to start.", ex);
        }

        // Its output is not passed on: it can quote settings.
        return result.ExitCode == 0
            ? ClaudePluginList.Parse(result.StandardOutput, workingDirectory)
            : throw new AgentStateException($"'claude plugin list --json' exited with code {result.ExitCode}.");
    }

    /// <inheritdoc />
    /// <remarks>A missing <c>.claude.json</c> means no servers; Claude Code creates it on first use.</remarks>
    /// <exception cref="AgentStateException">The file exists but cannot be read or is not recognized.</exception>
    public Task<IReadOnlyList<InstalledMcpServer>> GetMcpServersAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!fileSystem.FileExists(configFile))
        {
            return Task.FromResult<IReadOnlyList<InstalledMcpServer>>([]);
        }

        var json = fileSystem.ReadAllText(configFile)
            ?? throw new AgentStateException($"Claude Code's configuration file '{configFile}' cannot be read.");
        return Task.FromResult(ClaudeConfig.ReadMcpServers(json, ClaudeConfig.ProjectFolder(fileSystem, workingDirectory)));
    }

    /// <inheritdoc />
    public Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct) =>
        throw new NotImplementedException("Installing plugins with Claude Code comes in a later work unit.");

    /// <inheritdoc />
    public Task AddMcpServerAsync(McpServerRegistration registration, CancellationToken ct) =>
        throw new NotImplementedException("Registering MCP servers with Claude Code comes in a later work unit.");
}
