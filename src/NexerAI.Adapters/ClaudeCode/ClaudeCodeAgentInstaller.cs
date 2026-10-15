using System.ComponentModel;
using NexerAI.Adapters.Processes;
using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.ClaudeCode;

/// <summary>
/// The Claude Code adapter of <see cref="IAgentInstaller"/> for one working directory (design 4.4). Plugins come from
/// <c>claude plugin list --json</c>; MCP servers from <c>.claude.json</c>, read without starting them. Plugins are
/// installed with <c>claude plugin install</c>; registering MCP servers comes in the next work unit.
/// </summary>
public sealed class ClaudeCodeAgentInstaller : IAgentInstaller
{
    private const string SetupPage = "https://code.claude.com/docs/en/setup";

    /// <summary>
    /// The native installer puts <c>claude.exe</c> on <c>PATH</c>; npm, still a supported install
    /// (https://code.claude.com/docs/en/setup), puts the <c>claude.cmd</c> launcher there
    /// (https://code.claude.com/docs/en/troubleshoot-install). An <c>.exe</c> comes first, as with the default <c>PATHEXT</c>.
    /// </summary>
    private static readonly string[] ClaudeFileNames = ["claude.exe", "claude.cmd"];

    private readonly IProcessRunner processRunner;

    private readonly IReadOnlyFileSystem fileSystem;

    private readonly string configFile;

    private readonly string workingDirectory;

    private readonly string? searchPath;

    /// <summary>Creates the adapter.</summary>
    /// <param name="processRunner">Runs <c>claude</c>.</param>
    /// <param name="fileSystem">Reads <paramref name="configFile"/> and the git files that locate the repository root.</param>
    /// <param name="configFile">Absolute path of <c>.claude.json</c>, usually <see cref="DefaultConfigFile"/>.</param>
    /// <param name="workingDirectory">Absolute folder the commands act on: a drive path or a UNC path with a share.</param>
    /// <param name="searchPath">Where <c>claude</c> is looked for, usually <see cref="DefaultSearchPath"/>.</param>
    public ClaudeCodeAgentInstaller(
        IProcessRunner processRunner, IReadOnlyFileSystem fileSystem, string configFile, string workingDirectory, string? searchPath)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(configFile);
        ArgumentNullException.ThrowIfNull(workingDirectory);
        if (!Path.IsPathFullyQualified(configFile))
        {
            throw new ArgumentException($"The Claude Code configuration file '{configFile}' is not absolute.", nameof(configFile));
        }

        if (!ClaudeConfig.IsAbsoluteFolder(workingDirectory))
        {
            throw new ArgumentException($"The working directory '{workingDirectory}' is not absolute.", nameof(workingDirectory));
        }

        this.processRunner = processRunner;
        this.fileSystem = fileSystem;
        this.configFile = configFile;
        this.workingDirectory = workingDirectory;
        this.searchPath = searchPath;
    }

    /// <summary>The <c>PATH</c> environment variable.</summary>
    public static string? DefaultSearchPath => Environment.GetEnvironmentVariable("PATH");

    /// <summary>The <c>.claude.json</c> of the current user, honoring <c>CLAUDE_CONFIG_DIR</c> (see <see cref="ClaudeConfig.FilePath"/>).</summary>
    public static string DefaultConfigFile => ClaudeConfig.FilePath(
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <inheritdoc />
    /// <exception cref="AgentStateException"><c>claude</c> is not on <c>PATH</c>, cannot be started, fails, or prints output that is not recognized.</exception>
    public async Task<IReadOnlyList<InstalledPlugin>> GetInstalledPluginsAsync(CancellationToken ct)
    {
        var result = await RunClaudeAsync(["plugin", "list", "--json"], ct).ConfigureAwait(false);

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
    /// <remarks>
    /// Runs <c>claude plugin install &lt;name&gt;@&lt;marketplace&gt; --scope &lt;scope&gt; --json</c> with one
    /// <c>--config key=value</c> per configuration value, ordered by key: the values are on the command line, so they
    /// must not be secrets (https://code.claude.com/docs/en/plugins/cli-reference#plugin-install). A plugin already
    /// installed at that scope is a success. The planner never asks for a disabled one, which this would enable again.
    /// </remarks>
    /// <exception cref="AgentStateException">
    /// <c>claude</c> cannot be run, the result is <c>failed</c>, a configuration value was not applied, the command
    /// exits with another code than 0, or it prints no result line. The message names the plugin and the failure
    /// code, never the output, which can quote configuration values.
    /// </exception>
    public async Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var id = $"{request.Name}@{request.Marketplace}";
        List<string> arguments = ["plugin", "install", id, "--scope", ScopeName(request.Scope), "--json"];
        foreach (var (key, value) in request.Config.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            arguments.AddRange(["--config", $"{key}={value}"]);
        }

        var result = await RunClaudeAsync(arguments, ct).ConfigureAwait(false);
        var outcome = ClaudePluginResult.Parse(result.StandardOutput);
        if (outcome is { Succeeded: false })
        {
            var code = outcome.FailureCode is { } failureCode ? $" (failure code {failureCode})" : string.Empty;
            throw new AgentStateException($"Claude Code could not install the plugin {id}{code}.");
        }

        if (result.ExitCode != 0)
        {
            throw new AgentStateException($"'claude plugin install {id}' exited with code {result.ExitCode}.");
        }

        if (outcome is null)
        {
            throw new AgentStateException($"The output of 'claude plugin install {id} --json' has no result line; this version of nexer-ai does not recognize it.");
        }

        if (outcome.ConfigApplied == false)
        {
            throw new AgentStateException($"Claude Code installed the plugin {id} but did not apply its configuration values; 'claude plugin configure {id}' lists its options.");
        }
    }

    /// <summary>
    /// Runs <c>claude</c> with <paramref name="arguments"/> in the working directory: plugins at local scope belong to
    /// that exact folder, and MCP servers at local scope to its repository root (design 4.4).
    /// </summary>
    /// <exception cref="AgentStateException"><c>claude</c> is not on <c>PATH</c>, cannot be started or cannot be run safely.</exception>
    private async Task<ProcessResult> RunClaudeAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var claude = FindClaude() ?? throw new AgentStateException(
            $@"Claude Code was not found: no claude.exe or claude.cmd in the folders on PATH. Install it (the native installer puts claude.exe in %USERPROFILE%\.local\bin, see {SetupPage}), open a new terminal and try again.");
        try
        {
            return await processRunner.RunAsync(claude, arguments, workingDirectory, ct).ConfigureAwait(false);
        }
        catch (Win32Exception ex)
        {
            throw new AgentStateException(ex.NativeErrorCode switch
            {
                ProcessRunner.ErrorDirectory => $"Claude Code cannot be started: the working directory '{workingDirectory}' does not exist.",
                ProcessRunner.ErrorFileNotFound or ProcessRunner.ErrorPathNotFound => $"Claude Code cannot be started: '{claude}' no longer exists. Install it again (see {SetupPage}).",
                _ => $"Claude Code at '{claude}' cannot be started (Windows error {ex.NativeErrorCode}: {ex.Message}).",
            }, ex);
        }
        catch (ArgumentException ex)
        {
            throw new AgentStateException($"Claude Code at '{claude}' cannot be run safely: {ex.Message}", ex);
        }
    }

    private static string ScopeName(PluginScope scope) => scope switch
    {
        PluginScope.User => "user",
        PluginScope.Local => "local",
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Undefined scope."),
    };

    /// <summary>
    /// The first <see cref="ClaudeFileNames"/> match in the folders of <see cref="searchPath"/>, folder by folder like
    /// <c>cmd.exe</c>. Unlike <c>cmd.exe</c> and <c>CreateProcess</c>, neither the current nor the working directory
    /// is searched, nor a relative entry: a cloned repository could plant a <c>claude.exe</c>.
    /// </summary>
    private string? FindClaude()
    {
        foreach (var entry in (searchPath ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var folder = entry.Trim('"');
            if (!Path.IsPathFullyQualified(folder))
            {
                continue;
            }

            foreach (var name in ClaudeFileNames)
            {
                var file = Path.Combine(folder, name);
                if (fileSystem.FileExists(file))
                {
                    return file;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task AddMcpServerAsync(McpServerRegistration registration, CancellationToken ct) =>
        throw new NotImplementedException("Registering MCP servers with Claude Code comes in a later work unit.");
}
