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
    public async Task UpdateAsync_refuses_a_file_that_is_not_valid_UTF8_and_leaves_it_untouched()
    {
        // A hand-edited ANSI file: decoding it leniently would turn the accent into U+FFFD and write that back.
        byte[] ansi = [(byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xE9, (byte)'"', (byte)'}'];
        File.WriteAllBytes(temp.WriteFile(@".claude\settings.json"), ansi);
        var called = false;

        var error = await Assert.ThrowsAsync<AgentStateException>(() => new ClaudeSettingsFile(FilePath).UpdateAsync(
            text =>
            {
                called = true;
                return text + " ";
            },
            TestContext.Current.CancellationToken));

        Assert.False(called);
        Assert.Contains(FilePath, error.Message, StringComparison.Ordinal);
        Assert.Contains("UTF-8", error.Message, StringComparison.Ordinal);
        Assert.Equal(ansi, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_refuses_a_file_that_becomes_invalid_UTF8_while_it_works_and_leaves_it_as_it_is()
    {
        temp.WriteFile(@".claude\settings.json", "{}");
        byte[] ansi = [(byte)'{', 0xE9, (byte)'}'];

        var error = await Assert.ThrowsAsync<AgentStateException>(() => new ClaudeSettingsFile(FilePath).UpdateAsync(
            text =>
            {
                File.WriteAllBytes(FilePath, ansi);
                return "mine";
            },
            TestContext.Current.CancellationToken));

        Assert.Contains("UTF-8", error.Message, StringComparison.Ordinal);
        Assert.Equal(ansi, File.ReadAllBytes(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_still_reads_a_valid_UTF8_file_with_accents()
    {
        File.WriteAllBytes(temp.WriteFile(@".claude\settings.json"), Encoding.UTF8.GetBytes("{\"a\":\"é\"}"));
        string? seen = null;

        await new ClaudeSettingsFile(FilePath).UpdateAsync(text => (seen = text) + "x", TestContext.Current.CancellationToken);

        Assert.Equal("{\"a\":\"é\"}", seen);
    }

    [Fact]
    public async Task UpdateAsync_retries_while_another_program_holds_the_file_open_without_delete_sharing_and_then_succeeds()
    {
        // On Windows such a handle makes the move fail with an access error as often as with a sharing violation.
        temp.WriteFile(@".claude\settings.json", "a");
        FileStream? held = null;

        var written = await new ClaudeSettingsFile(FilePath).UpdateAsync(
            text =>
            {
                if (held is null)
                {
                    held = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    _ = Task.Delay(80).ContinueWith(_ => held.Dispose(), TaskScheduler.Default);
                }

                return text + "+";
            },
            TestContext.Current.CancellationToken);

        Assert.True(written);
        Assert.Equal("a+", File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_says_the_file_was_busy_or_changing_when_the_retries_run_out_and_leaves_it_alone()
    {
        temp.WriteFile(@".claude\settings.json", "a");
        using var held = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var error = await Assert.ThrowsAsync<AgentStateException>(
            () => new ClaudeSettingsFile(FilePath).UpdateAsync(text => text + "+", TestContext.Current.CancellationToken));

        Assert.Contains("busy", error.Message, StringComparison.Ordinal);
        Assert.Contains(FilePath, error.Message, StringComparison.Ordinal);
        held.Dispose();
        Assert.Equal("a", File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_still_fails_at_once_for_a_read_only_file()
    {
        temp.WriteFile(@".claude\settings.json", "a");
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        try
        {
            var error = await Assert.ThrowsAsync<AgentStateException>(
                () => new ClaudeSettingsFile(FilePath).UpdateAsync(text => text + "+", TestContext.Current.CancellationToken));

            Assert.Contains("cannot be written", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(FilePath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task UpdateAsync_fails_at_once_with_an_access_denied_message_when_permissions_deny_writing_the_file()
    {
        // R1: a permanent denial (an ACL) is not a busy file: no retries, and the message must not advise trying again.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        temp.WriteFile(@".claude\settings.json", "a");
        var info = new FileInfo(FilePath);
        var folder = new DirectoryInfo(Path.GetDirectoryName(FilePath)!);
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var denyFile = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.WriteData | System.Security.AccessControl.FileSystemRights.Delete,
            System.Security.AccessControl.AccessControlType.Deny);
        var denyChildren = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles,
            System.Security.AccessControl.AccessControlType.Deny);
        var fileSecurity = info.GetAccessControl();
        fileSecurity.AddAccessRule(denyFile);
        info.SetAccessControl(fileSecurity);
        var folderSecurity = folder.GetAccessControl();
        folderSecurity.AddAccessRule(denyChildren);
        folder.SetAccessControl(folderSecurity);
        try
        {
            var calls = 0;
            var error = await Assert.ThrowsAsync<AgentStateException>(
                () => new ClaudeSettingsFile(FilePath).UpdateAsync(
                    text =>
                    {
                        calls++;
                        return text + "+";
                    },
                    TestContext.Current.CancellationToken));

            Assert.Equal(1, calls);
            Assert.Contains("access", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("denied", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Try again", error.Message, StringComparison.Ordinal);
            Assert.Contains(FilePath, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            var restoreFolder = folder.GetAccessControl();
            restoreFolder.RemoveAccessRule(denyChildren);
            folder.SetAccessControl(restoreFolder);
            var restoreFile = info.GetAccessControl();
            restoreFile.RemoveAccessRule(denyFile);
            info.SetAccessControl(restoreFile);
        }
    }

    [Fact]
    public async Task UpdateAsync_writes_through_a_link_to_the_file_it_points_at_and_keeps_the_link()
    {
        // The seam stands in for a symbolic link, which needs privileges a test run may not have.
        var real = temp.WriteFile(@"dotfiles\settings.json", "a");
        temp.WriteFile(@".claude\settings.json", "link placeholder");
        var file = new ClaudeSettingsFile(FilePath, path => path == FilePath ? real : null);

        var written = await file.UpdateAsync(text => text + "+", TestContext.Current.CancellationToken);

        Assert.True(written);
        Assert.Equal("a+", File.ReadAllText(real));
        Assert.Equal("link placeholder", File.ReadAllText(FilePath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(real)!));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(FilePath)!));
    }

    [Fact]
    public async Task UpdateAsync_creates_the_file_a_dangling_link_points_at()
    {
        var real = Path.Combine(temp.Path, "dotfiles", "settings.json");
        var file = new ClaudeSettingsFile(FilePath, path => path == FilePath ? real : null);

        await file.UpdateAsync(text => text is null ? "{}" : null, TestContext.Current.CancellationToken);

        Assert.Equal("{}", File.ReadAllText(real));
    }

    [Fact]
    public async Task UpdateAsync_keeps_a_real_symbolic_link_when_the_system_lets_the_test_create_one()
    {
        var real = temp.WriteFile(@"dotfiles\settings.json", "a");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        try
        {
            File.CreateSymbolicLink(FilePath, real);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("Creating a symbolic link needs administrator rights or Developer Mode.");
        }

        await new ClaudeSettingsFile(FilePath).UpdateAsync(text => text + "+", TestContext.Current.CancellationToken);

        Assert.NotNull(new FileInfo(FilePath).LinkTarget);
        Assert.Equal("a+", File.ReadAllText(real));
    }

    [Fact]
    public void Constructor_rejects_a_relative_path()
    {
        Assert.Throws<ArgumentException>(() => new ClaudeSettingsFile(@"settings.json"));
    }
}
