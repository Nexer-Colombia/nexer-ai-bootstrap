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

    [Theory]
    [InlineData("\"")]
    [InlineData("!")]
    [InlineData("^")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("a b !c")]
    [InlineData("a \"b\" c")]
    [InlineData("\uFF02")] // Fullwidth quotation mark: a best-fit codepage can map it to a quote.
    [InlineData("\uFF05")] // Fullwidth percent sign.
    [InlineData("\uFF3E")] // Fullwidth circumflex accent.
    [InlineData("\u01C3")] // Latin letter retroflex click: best-fit maps it to "!".
    [InlineData("\u02BA")] // Modifier letter double prime: best-fit maps it to a quote.
    [InlineData("\u02EE")] // Modifier letter double apostrophe.
    [InlineData("\u02C6")] // Modifier letter circumflex accent: best-fit maps it to "^".
    [InlineData("\u00D7")] // Multiplication sign: inside the Latin-1 letter block but not a letter.
    [InlineData("\u00F7")] // Division sign.
    public async Task RunAsync_refuses_a_batch_file_argument_with_a_character_cmd_or_a_codepage_could_turn_into_syntax(string argument)
    {
        var path = Path.Combine(folder.Path, "tool.cmd");
        File.WriteAllText(path, "@exit /b 0\r\n");

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(path, [argument], folder.Path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("C:/Users/Mar\u00EDa/nexer-ai.exe")]
    [InlineData("Mu\u00F1oz")]
    [InlineData("Zo\u00EB")]
    [InlineData("\u00C7elik")]
    [InlineData("\u00D1u\u00F1ez")]
    [InlineData("Mar\u00EDa Jos\u00E9")]
    public async Task RunAsync_accepts_letters_and_digits_beyond_ASCII_in_a_batch_file_argument(string argument)
    {
        var path = Path.Combine(folder.Path, "tool.cmd");
        File.WriteAllText(path, "@exit /b 0\r\n");

        var result = await runner.RunAsync(path, [argument], folder.Path, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("a&b")]
    [InlineData("a|b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a^b")]
    [InlineData("a%b")]
    [InlineData("a!b")]
    [InlineData("a\"b")]
    public async Task RunAsync_refuses_a_batch_file_path_with_a_character_cmd_would_interpret(string folderName)
    {
        // Nothing is created: the refusal comes before the file is touched, and some of these are not valid file names.
        var path = Path.Combine(folder.Path, folderName, "tool.cmd");

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(path, ["list"], folder.Path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(@"\\server\share\repo")]
    [InlineData("//server/share/repo")]
    [InlineData(@"\/server\share\repo")]
    [InlineData(@"/\server\share\repo")]
    public async Task RunAsync_refuses_a_UNC_working_directory_for_a_batch_file(string workingDirectory)
    {
        // cmd.exe does not start in a UNC folder: it runs in the Windows folder instead, so a local-scope command
        // would act on the wrong folder.
        var path = Path.Combine(folder.Path, "tool.cmd");
        File.WriteAllText(path, "@exit /b 0\r\n");

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(path, ["list"], workingDirectory, TestContext.Current.CancellationToken));

        Assert.Contains("UNC", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_writes_the_standard_input_then_closes_it()
    {
        var result = await runner.RunAsync(
            "findstr.exe", ["^"], folder.Path, TestContext.Current.CancellationToken, standardInput: "{\"k\":\"v\"}");

        Assert.Equal(new ProcessResult(0, "{\"k\":\"v\"}\r\n", string.Empty), result);
    }

    [Fact]
    public async Task RunAsync_kills_the_program_and_throws_when_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync("cmd.exe", ["/c", "ping -n 30 127.0.0.1 >nul"], folder.Path, cancel.Token));
    }

    [Fact]
    public async Task RunAsync_releases_the_working_directory_before_it_throws_the_cancellation()
    {
        // cmd.exe and its child ping.exe both run in the folder; the marker says they started, so the cancellation
        // reaches a running tree. A caller may delete the folder as soon as the cancellation surfaces.
        var workingDirectory = Path.Combine(folder.Path, "repo");
        Directory.CreateDirectory(workingDirectory);
        var started = Path.Combine(workingDirectory, "started");
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var run = runner.RunAsync("cmd.exe", ["/c", "type nul> started& ping -n 30 127.0.0.1 >nul"], workingDirectory, cancel.Token);
        await WaitUntilAsync(() => File.Exists(started));
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Directory.Delete(workingDirectory, recursive: true);
    }

    [Fact]
    public async Task RunAsync_throws_the_cancellation_even_when_the_program_has_already_exited()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await cancel.CancelAsync();

        // The program may or may not have exited when the runner kills it; either way the cancellation surfaces.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync("cmd.exe", ["/c", "exit 0"], folder.Path, cancel.Token));
    }

    [Fact]
    public async Task RunAsync_does_not_deadlock_when_the_program_echoes_input_larger_than_the_pipes()
    {
        var input = string.Concat(Enumerable.Repeat(new string('x', 99) + "\n", 50_000)); // About 5 MB.

        var result = await runner.RunAsync(
            "findstr.exe", ["^"], folder.Path, TestContext.Current.CancellationToken, standardInput: input)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(input.Length, result.StandardOutput.Replace("\r\n", "\n").Length);
    }

    [Fact]
    public async Task RunAsync_observes_cancellation_while_writing_input_to_a_program_that_never_reads_it()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(300));
        var input = new string('x', 8_000_000); // Far more than a pipe holds.

        var run = runner.RunAsync(
            "cmd.exe", ["/c", "ping -n 30 127.0.0.1 >nul"], folder.Path, cancel.Token, standardInput: input);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
    }

    /// <summary>Polls <paramref name="condition"/> until it holds; fails after ten seconds.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
