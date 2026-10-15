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
}
