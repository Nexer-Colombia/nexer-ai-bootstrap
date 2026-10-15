using System.ComponentModel;
using System.Text;
using System.Text.Json;
using NexerAI.Adapters.Processes;
using NexerAI.Core.ClaudeCode;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.ClaudeCode;

/// <summary>
/// The Claude Code adapter of <see cref="IAgentInstaller"/> for one working directory (design 4.4). Plugins come from
/// <c>claude plugin list --json</c>; MCP servers from <c>.claude.json</c>, read without starting them. Changes go
/// through <c>claude plugin install</c> and <c>claude mcp add</c> and <c>remove</c>.
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

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(30);

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
        return Task.FromResult(ReadConfig() is { } json
            ? ClaudeConfig.ReadMcpServers(json, ClaudeConfig.ProjectFolder(fileSystem, workingDirectory))
            : []);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Idempotent. A server of that name and scope that already runs the same command and arguments is left alone;
    /// one with another command line is removed with <c>claude mcp remove --scope &lt;scope&gt; &lt;name&gt;</c> first,
    /// because <c>claude mcp add</c> refuses an existing name (observed on 2.1.300: exit code 1, nothing changed). Then
    /// <c>claude mcp add --transport stdio --scope &lt;scope&gt; &lt;name&gt; -- &lt;command&gt; [args...]</c>
    /// (https://code.claude.com/docs/en/mcp#option-3-add-a-local-stdio-server). Never <c>--env</c>: Claude Code
    /// stores its values in plain text, so a secret reaches the server through the launcher instead (design 4.4).
    /// When the <c>add</c> after a <c>remove</c> fails or is cancelled, the previous stdio command line (read from
    /// <c>.claude.json</c>, never its <c>env</c>) is registered again once, and the error says whether it was restored.
    /// </remarks>
    /// <exception cref="AgentStateException">
    /// <c>.claude.json</c> cannot be read or is not recognized, <c>claude</c> cannot be run, or a command exits with
    /// another code than 0. The message never quotes the output, which repeats the command line.
    /// </exception>
    public async Task AddMcpServerAsync(McpServerRegistration registration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ct.ThrowIfCancellationRequested();

        var scope = ScopeName(registration.Scope);
        McpServerRegistration? previous = null;
        var removed = false;
        if (ReadConfig() is { } json)
        {
            var projectFolder = ClaudeConfig.ProjectFolder(fileSystem, workingDirectory);
            if (ClaudeConfig.ReadMcpServers(json, projectFolder).Contains(new InstalledMcpServer(registration.Name, registration.Scope)))
            {
                if (ClaudeConfig.RunsCommand(json, projectFolder, registration))
                {
                    return;
                }

                previous = ClaudeConfig.ReadMcpCommandLine(json, projectFolder, registration.Name, registration.Scope);
                await RunMcpCommandAsync("remove", [], scope, registration.Name, [], ct).ConfigureAwait(false);
                removed = true;
            }
        }

        try
        {
            await AddAsync(registration, scope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (removed && (ex is AgentStateException || (ex is OperationCanceledException && ct.IsCancellationRequested)))
        {
            // The old server is gone: put it back, so a failed or cancelled replacement does not leave the person without it.
            var restored = await TryRestoreAsync(previous, scope).ConfigureAwait(false);
            var cause = ex is OperationCanceledException ? "was cancelled" : "failed";
            throw new AgentStateException(
                $"Replacing the MCP server '{registration.Name}' ({scope} scope) {cause} after its old entry was removed; "
                + (restored
                    ? "the previous entry was restored."
                    : "the previous entry could not be restored, so the server is now missing: run 'nexer-ai project' again to register it."),
                ex);
        }
    }

    private Task AddAsync(McpServerRegistration registration, string scope, CancellationToken ct) =>
        RunMcpCommandAsync(
            "add", ["--transport", "stdio"], scope, registration.Name, ["--", registration.Command, .. registration.Arguments], ct);

    /// <summary>
    /// Registers <paramref name="previous"/> again, once, with its own short timeout: the token of the caller may be the
    /// reason the replacement stopped. Never throws; <see langword="false"/> when there is nothing to restore or it failed.
    /// </summary>
    private async Task<bool> TryRestoreAsync(McpServerRegistration? previous, string scope)
    {
        if (previous is null)
        {
            return false;
        }

        using var timeout = new CancellationTokenSource(RestoreTimeout);
        try
        {
            await AddAsync(previous, scope, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is AgentStateException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>The text of <c>.claude.json</c>, or <see langword="null"/> when it does not exist yet: Claude Code creates it on first use.</summary>
    private string? ReadConfig() =>
        !fileSystem.FileExists(configFile)
            ? null
            : fileSystem.ReadAllText(configFile)
                ?? throw new AgentStateException($"Claude Code's configuration file '{configFile}' cannot be read.");

    /// <summary>
    /// Runs <c>claude mcp &lt;subcommand&gt; [options] --scope &lt;scope&gt; &lt;name&gt; [rest]</c>; the error names
    /// the subcommand, scope and server, never the rest of the command line.
    /// </summary>
    private async Task RunMcpCommandAsync(
        string subcommand, IReadOnlyList<string> options, string scope, string name, IReadOnlyList<string> rest, CancellationToken ct)
    {
        var result = await RunClaudeAsync(["mcp", subcommand, .. options, "--scope", scope, name, .. rest], ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new AgentStateException($"'claude mcp {subcommand} --scope {scope} {name}' exited with code {result.ExitCode}.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Runs <c>claude plugin install &lt;name&gt;@&lt;marketplace&gt; --scope &lt;scope&gt; --json</c> with one
    /// <c>--config key=value</c> per configuration value, ordered by key: the values are on the command line, so they
    /// must not be secrets (https://code.claude.com/docs/en/plugins/cli-reference#plugin-install). A plugin already
    /// installed at that scope is a success; since <c>install</c> then ignores <c>--config</c> (no <c>configApplied</c> in
    /// its result), the values are saved with <c>claude plugin configure &lt;id&gt; --values-stdin --json</c>, a JSON
    /// object on standard input, never on the command line
    /// (https://code.claude.com/docs/en/plugins/cli-reference#plugin-configure, Claude Code 2.1.285 or later).
    /// The planner never asks for a disabled plugin, which this would enable again.
    /// </remarks>
    /// <exception cref="ArgumentException">A configuration key is empty or holds <c>=</c> or whitespace. The value is never quoted.</exception>
    /// <exception cref="AgentStateException">
    /// <c>claude</c> cannot be run, the result is <c>failed</c>, a configuration value was not applied, the command
    /// exits with another code than 0, or it prints no result line. The message names the plugin and the failure
    /// code, never the output, which can quote configuration values.
    /// </exception>
    public async Task InstallPluginAsync(PluginInstallRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var key in request.Config.Keys)
        {
            if (key.Length == 0 || key.Contains('=', StringComparison.Ordinal) || key.Any(char.IsWhiteSpace))
            {
                throw new ArgumentException("A plugin configuration key must not be empty or contain '=' or whitespace.", nameof(request));
            }
        }

        ct.ThrowIfCancellationRequested();

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

        if (request.Config.Count == 0)
        {
            return;
        }

        if (outcome.ConfigApplied == false)
        {
            throw new AgentStateException($"Claude Code installed the plugin {id} but did not apply its configuration values; 'claude plugin configure {id}' lists its options.");
        }

        if (outcome.ConfigApplied is null)
        {
            // Already installed: install left its options alone.
            await ConfigurePluginAsync(id, request.Config, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs <c>claude plugin configure &lt;id&gt; --values-stdin --json</c> with the values as a JSON object on standard
    /// input. The error names the plugin and the exit code, never a value.
    /// </summary>
    private async Task ConfigurePluginAsync(string id, IReadOnlyDictionary<string, string> config, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in config.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        var result = await RunClaudeAsync(["plugin", "configure", id, "--values-stdin", "--json"], ct, Encoding.UTF8.GetString(buffer.ToArray()))
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new AgentStateException(
                $"The plugin {id} is installed but its options were not applied: 'claude plugin configure {id} --values-stdin' exited with code {result.ExitCode}.");
        }
    }

    /// <summary>
    /// Runs <c>claude</c> with <paramref name="arguments"/> in the working directory: plugins at local scope belong to
    /// that exact folder, and MCP servers at local scope to its repository root (design 4.4).
    /// </summary>
    /// <exception cref="AgentStateException"><c>claude</c> is not on <c>PATH</c>, cannot be started or cannot be run safely.</exception>
    private async Task<ProcessResult> RunClaudeAsync(IReadOnlyList<string> arguments, CancellationToken ct, string? standardInput = null)
    {
        var claude = FindClaude() ?? throw new AgentStateException(
            $@"Claude Code was not found: no claude.exe or claude.cmd in the folders on PATH. Install it (the native installer puts claude.exe in %USERPROFILE%\.local\bin, see {SetupPage}), open a new terminal and try again.");
        try
        {
            return await processRunner.RunAsync(claude, arguments, workingDirectory, ct, standardInput).ConfigureAwait(false);
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
}
