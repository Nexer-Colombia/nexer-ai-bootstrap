using System.ComponentModel;
using NexerAI.Adapters.Processes;

namespace NexerAI.Adapters.Tests.Processes;

/// <summary>Runs <c>cmd.exe</c> only; nothing here reads or changes the machine's configuration.</summary>
public sealed class ProcessRunnerTests : IDisposable
{
    private readonly TempDirectory folder = new();

    private readonly ProcessRunner runner = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public async Task RunAsync_returns_the_exit_code_and_both_outputs()
    {
        var result = await runner.RunAsync("cmd.exe", ["/c", "echo out& echo err 1>&2& exit 3"], folder.Path, TestContext.Current.CancellationToken);

        Assert.Equal(new ProcessResult(3, "out\r\n", "err \r\n"), result);
    }

    [Fact]
    public async Task RunAsync_starts_the_program_in_the_working_directory()
    {
        var result = await runner.RunAsync("cmd.exe", ["/c", "cd"], folder.Path, TestContext.Current.CancellationToken);

        Assert.Equal(folder.Path, result.StandardOutput.TrimEnd());
    }

    [Fact]
    public async Task RunAsync_throws_when_the_program_cannot_be_started()
    {
        await Assert.ThrowsAsync<Win32Exception>(
            () => runner.RunAsync("nexer-ai-no-such-program", [], folder.Path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RunAsync_reports_a_missing_working_directory_with_its_own_error_code()
    {
        var error = await Assert.ThrowsAsync<Win32Exception>(
            () => runner.RunAsync("cmd.exe", ["/c", "exit 0"], Path.Combine(folder.Path, "gone"), TestContext.Current.CancellationToken));

        Assert.Equal(ProcessRunner.ErrorDirectory, error.NativeErrorCode);
    }

    [Fact]
    public async Task RunAsync_runs_a_cmd_file_given_by_its_full_path()
    {
        File.WriteAllText(Path.Combine(folder.Path, "tool.cmd"), "@echo %*\r\n");

        var result = await runner.RunAsync(
            Path.Combine(folder.Path, "tool.cmd"), ["plugin", "install", "a@m", "--scope", "local"], folder.Path, TestContext.Current.CancellationToken);

        Assert.Equal(new ProcessResult(0, "plugin install a@m --scope local\r\n", string.Empty), result);
    }

    [Fact]
    public async Task RunAsync_passes_a_batch_file_argument_with_spaces_as_one_quoted_argument()
    {
        // A folder with a space, as in C:\Users\Ana Maria\AppData\Roaming\npm\claude.cmd, and arguments with spaces:
        // the command line then holds more than two quotes, which cmd.exe /c must not strip.
        var tool = Path.Combine(folder.Path, "Ana Maria", "tool.cmd");
        Directory.CreateDirectory(Path.GetDirectoryName(tool)!);
        File.WriteAllText(tool, "@for %%a in (%*) do @echo [%%~a]\r\n");

        var result = await runner.RunAsync(
            tool, ["--", "C:/Users/Ana Maria/nexer-ai.exe", "mcp-launch", "a  b "], folder.Path, TestContext.Current.CancellationToken);

        Assert.Equal(new ProcessResult(0, "[--]\r\n[C:/Users/Ana Maria/nexer-ai.exe]\r\n[mcp-launch]\r\n[a  b ]\r\n", string.Empty), result);
    }

    [Theory]
    [InlineData("tool.cmd", "a&echo injected")]
    [InlineData("tool.cmd", "a b&echo injected")]
    [InlineData("tool.cmd", "a b\" & echo injected & \"")]
    [InlineData("tool.cmd", "%PATH%")]
    [InlineData("tool.cmd", "a %PATH%")]
    [InlineData("tool.cmd", "a\tb")]
    [InlineData("tool.bat", "x|y")]
    [InlineData("50%.cmd", "list")]
    public async Task RunAsync_refuses_a_batch_file_with_text_that_cmd_would_interpret(string file, string argument)
    {
        var path = Path.Combine(folder.Path, file);
        File.WriteAllText(path, $"@echo ran> \"{Path.Combine(folder.Path, "ran.txt")}\"\r\n");

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(path, [argument], folder.Path, TestContext.Current.CancellationToken));

        Assert.False(File.Exists(Path.Combine(folder.Path, "ran.txt")));
    }

    [Fact]
    public async Task RunAsync_kills_the_program_and_throws_when_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        // Not the temporary folder: a killed process can hold its working directory for a moment, failing the cleanup.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync("cmd.exe", ["/c", "ping -n 30 127.0.0.1 >nul"], Environment.SystemDirectory, cancel.Token));
    }

    [Fact]
    public async Task RunAsync_throws_the_cancellation_even_when_the_program_has_already_exited()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancel.CancelAsync();

        // The program may or may not have exited when the runner kills it; either way the cancellation surfaces.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync("cmd.exe", ["/c", "exit 0"], Environment.SystemDirectory, cancel.Token));
    }
}
