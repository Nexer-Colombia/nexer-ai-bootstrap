using System.Text;
using NexerAI.Adapters.ClaudeCode;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.ClaudeCode;

public sealed class ClaudeSettingsFileTests : IDisposable
{
    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private string FilePath => Path.Combine(temp.Path, ".claude", "settings.json");

    [Fact]
    public async Task UpdateAsync_replaces_the_file_with_the_changed_text_as_UTF8_without_a_byte_order_mark()
    {
        temp.WriteFile(@".claude\settings.json", "{}");

        var written = await new ClaudeSettingsFile(FilePath).UpdateAsync(text => text + " é", TestContext.Current.CancellationToken);

        Assert.True(written);
        Assert.Equal(Encoding.UTF8.GetBytes("{} é"), File.ReadAllBytes(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_passes_the_text_without_its_byte_order_mark()
    {
        File.WriteAllBytes(temp.WriteFile(@".claude\settings.json"), [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']);
        string? seen = null;

        await new ClaudeSettingsFile(FilePath).UpdateAsync(text => (seen = text) + "\n", TestContext.Current.CancellationToken);

        Assert.Equal("{}", seen);
    }

    [Fact]
    public async Task UpdateAsync_passes_null_for_a_missing_file_and_creates_its_folder()
    {
        string? seen = "unset";

        var written = await new ClaudeSettingsFile(FilePath).UpdateAsync(text => (seen = text) is null ? "{}\n" : null, TestContext.Current.CancellationToken);

        Assert.True(written);
        Assert.Null(seen);
        Assert.Equal("{}\n", File.ReadAllText(FilePath));
    }

    [Fact]
    public async Task UpdateAsync_writes_nothing_when_the_change_returns_null()
    {
        temp.WriteFile(@".claude\settings.json", "{}");
        var before = File.GetLastWriteTimeUtc(FilePath);

        var written = await new ClaudeSettingsFile(FilePath).UpdateAsync(_ => null, TestContext.Current.CancellationToken);

        Assert.False(written);
        Assert.Equal(before, File.GetLastWriteTimeUtc(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_starts_again_from_the_new_text_when_the_file_changes_while_it_works()
    {
        // Claude Code rewrites the whole file when it saves a setting; that write must not be lost.
        temp.WriteFile(@".claude\settings.json", "a");
        var seen = new List<string?>();

        await new ClaudeSettingsFile(FilePath).UpdateAsync(
            text =>
            {
                seen.Add(text);
                if (seen.Count == 1)
                {
                    File.WriteAllText(FilePath, "b");
                }

                return text + "+";
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], seen);
        Assert.Equal("b+", File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_gives_up_with_a_typed_error_when_the_file_keeps_changing()
    {
        temp.WriteFile(@".claude\settings.json", "0");
        var count = 0;

        var error = await Assert.ThrowsAsync<AgentStateException>(() => new ClaudeSettingsFile(FilePath).UpdateAsync(
            text =>
            {
                File.WriteAllText(FilePath, $"{++count}");
                return "mine";
            },
            TestContext.Current.CancellationToken));

        Assert.Contains(FilePath, error.Message, StringComparison.Ordinal);
        Assert.NotEqual("mine", File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_lets_the_change_refuse_the_text_and_writes_nothing()
    {
        temp.WriteFile(@".claude\settings.json", "{");

        await Assert.ThrowsAsync<AgentStateException>(() => new ClaudeSettingsFile(FilePath).UpdateAsync(
            _ => throw new AgentStateException("refused"), TestContext.Current.CancellationToken));

        Assert.Equal("{", File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_reports_a_path_that_is_a_folder_as_a_typed_error()
    {
        Directory.CreateDirectory(FilePath);

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => new ClaudeSettingsFile(FilePath).UpdateAsync(_ => "{}", TestContext.Current.CancellationToken));

        Assert.Contains(FilePath, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_rejects_a_relative_path()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeSettingsFile(@"settings.json"));
    }
}
