using NexerAI.Adapters.State;
using NexerAI.Core.Domain;
using NexerAI.Core.State;

namespace NexerAI.Adapters.Tests.State;

public sealed class FileStateStoreTests : IDisposable
{
    private static readonly ProjectState Website = new("acme-website", ["nexer-dev-umbraco", "nexer-engram"], ["nexer-azure-devops"]);

    private readonly TempDirectory root = new();

    private readonly string statePath;

    private readonly FileStateStore store;

    public FileStateStoreTests()
    {
        statePath = Path.Combine(root.Path, ".nexer-ai", "state.json");
        store = new FileStateStore(statePath);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => root.Dispose();

    [Fact]
    public async Task ReadAsync_returns_the_empty_state_when_the_directory_or_the_file_is_missing()
    {
        Assert.Same(InstallState.Empty, await store.ReadAsync(Ct));

        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);

        Assert.Same(InstallState.Empty, await store.ReadAsync(Ct));
    }

    [Fact]
    public async Task UpdateAsync_creates_the_directory_and_writes_utf8_without_bom()
    {
        var written = await store.UpdateAsync(s => s.WithProject("github.com/acme/website", Website) with { Channel = Channel.Stable }, Ct);

        var bytes = await File.ReadAllBytesAsync(statePath, Ct);
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal(StateFileFormat.Serialize(written), System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task UpdateAsync_starts_from_the_stored_state_and_another_instance_reads_it_back()
    {
        await store.UpdateAsync(s => s with { Channel = Channel.Early, Role = Role.Qa }, Ct);
        await store.UpdateAsync(s => s.WithProject("github.com/acme/website", Website), Ct);

        var state = await new FileStateStore(statePath).ReadAsync(Ct);

        Assert.Same(Channel.Early, state.Channel);
        Assert.Equal(Role.Qa, state.Role);
        Assert.Equal(["nexer-dev-umbraco", "nexer-engram"], state.Projects["github.com/acme/website"].Plugins);
    }

    [Fact]
    public async Task ReadAsync_accepts_a_byte_order_mark()
    {
        root.WriteFile(@".nexer-ai\state.json", "\uFEFF{ \"schema\": 1, \"role\": \"po\" }");

        Assert.Equal(Role.ProductOwner, (await store.ReadAsync(Ct)).Role);
    }

    [Fact]
    public async Task A_corrupt_file_is_reported_with_its_path_and_left_untouched()
    {
        root.WriteFile(@".nexer-ai\state.json", "{ \"schema\": 2 }");
        var called = false;

        var read = await Assert.ThrowsAsync<StateFileException>(() => store.ReadAsync(Ct));
        var update = await Assert.ThrowsAsync<StateFileException>(() => store.UpdateAsync(s => { called = true; return s; }, Ct));

        Assert.All([read, update], ex => Assert.Contains(statePath, ex.Message, StringComparison.Ordinal));
        Assert.All([read, update], ex => Assert.Contains("schema version 2", ex.Message, StringComparison.Ordinal));
        Assert.IsType<StateFileException>(read.InnerException);
        Assert.False(called);
        Assert.Equal("{ \"schema\": 2 }", await File.ReadAllTextAsync(statePath, Ct));
    }

    [Fact]
    public async Task A_failing_update_leaves_the_file_untouched_and_no_temporary_files()
    {
        var before = await store.UpdateAsync(s => s.WithProject("github.com/acme/website", Website), Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(_ => throw new InvalidOperationException("boom"), Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(_ => null!, Ct));

        Assert.Equal(StateFileFormat.Serialize(before), await File.ReadAllTextAsync(statePath, Ct));
        AssertOnlyStateAndLockFiles();
    }

    [Fact]
    public async Task Concurrent_updates_from_two_instances_keep_every_change()
    {
        var other = new FileStateStore(statePath);

        await Task.WhenAll(Enumerable.Range(0, 40).Select(i => Task.Run(
            () => (i % 2 == 0 ? store : other).UpdateAsync(s => s.WithProject($"github.com/acme/repo-{i}", Website), Ct), Ct)));

        var state = await store.ReadAsync(Ct);
        Assert.Equal(40, state.Projects.Count);
        AssertOnlyStateAndLockFiles();
    }

    [Fact]
    public async Task UpdateAsync_waits_for_a_reader_to_close_the_file()
    {
        await store.UpdateAsync(s => s with { Role = Role.Qa }, Ct);
        var reader = OpenForReading();
        var release = Task.Run(
            async () =>
            {
                await Task.Delay(150, Ct);
                await reader.DisposeAsync();
            },
            Ct);

        await store.UpdateAsync(s => s with { Role = Role.Developer }, Ct);
        await release;

        Assert.Equal(Role.Developer, (await store.ReadAsync(Ct)).Role);
        AssertOnlyStateAndLockFiles();
    }

    [Fact]
    public async Task UpdateAsync_fails_cleanly_when_a_reader_keeps_the_file_open()
    {
        await store.UpdateAsync(s => s with { Role = Role.Qa }, Ct);
        var quick = new FileStateStore(statePath, TimeSpan.FromMilliseconds(200));

        using (OpenForReading())
        {
            var ex = await Assert.ThrowsAsync<IOException>(() => quick.UpdateAsync(s => s with { Role = Role.Developer }, Ct));
            Assert.Contains(statePath, ex.Message, StringComparison.Ordinal);
        }

        Assert.Equal(Role.Qa, (await store.ReadAsync(Ct)).Role);
        AssertOnlyStateAndLockFiles();
    }

    [Fact]
    public async Task UpdateAsync_times_out_while_another_process_holds_the_lock()
    {
        var quick = new FileStateStore(statePath, TimeSpan.FromMilliseconds(200));
        using var held = HoldLock();

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => quick.UpdateAsync(s => s with { Role = Role.Qa }, Ct));

        Assert.Contains(statePath, ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(statePath));
    }

    [Fact]
    public async Task UpdateAsync_stops_waiting_for_the_lock_when_cancelled()
    {
        using var held = HoldLock();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.UpdateAsync(s => s, cancellation.Token));

        Assert.False(File.Exists(statePath));
    }

    [Fact]
    public void Constructor_rejects_a_relative_path_and_a_negative_timeout()
    {
        Assert.Throws<ArgumentException>(() => new FileStateStore(@".nexer-ai\state.json"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileStateStore(statePath, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void DefaultPath_is_the_state_file_in_the_user_profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, ".nexer-ai", "state.json"), FileStateStore.DefaultPath);
    }

    private FileStream HoldLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        return new FileStream(statePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Opens the state file the way a well-behaved reader does, sharing read, write and delete.</summary>
    private FileStream OpenForReading() =>
        new(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private void AssertOnlyStateAndLockFiles()
    {
        var names = Directory.GetFiles(Path.GetDirectoryName(statePath)!).Select(Path.GetFileName).Order(StringComparer.Ordinal);
        Assert.Equal(["state.json", "state.json.lock"], names);
    }
}
