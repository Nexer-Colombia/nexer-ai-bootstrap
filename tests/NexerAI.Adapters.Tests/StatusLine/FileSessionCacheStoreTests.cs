using NexerAI.Adapters.StatusLine;
using NexerAI.Core.StatusLine;

namespace NexerAI.Adapters.Tests.StatusLine;

public sealed class FileSessionCacheStoreTests : IDisposable
{
    private const string Id = "00000000-0000-4000-8000-000000000001";

    private static readonly DateTimeOffset T = DateTimeOffset.FromUnixTimeSeconds(1791997772);

    private readonly TempDirectory temp = new();

    private string Folder => Path.Combine(temp.Path, "statusline");

    public void Dispose() => temp.Dispose();

    [Fact]
    public void Load_returns_null_when_nothing_was_saved()
    {
        Assert.Null(Store().Load(Id));
    }

    [Theory]
    [InlineData(1791997772L, true)]
    [InlineData(null, false)]
    public void Save_creates_the_folder_and_load_reads_the_session_back(long? lastResponseAt, bool renewalSeen)
    {
        var session = new CacheSession(3, lastResponseAt is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null, renewalSeen);

        Store().Save(Id, session);

        Assert.Equal(session, Store().Load(Id));
    }

    [Fact]
    public void Save_writes_one_small_json_file_named_after_the_session_and_no_temporary_file()
    {
        Store().Save(Id, new CacheSession(3, T, true));

        var file = Assert.Single(Directory.GetFiles(Folder));
        Assert.Equal(Path.Combine(Folder, Id + ".json"), file);
        Assert.Equal("{\"requests\":3,\"lastResponseAt\":1791997772,\"renewalSeen\":true}", File.ReadAllText(file));
    }

    [Fact]
    public void Save_replaces_the_previous_memory()
    {
        Store().Save(Id, new CacheSession(1, T, true));
        Store().Save(Id, new CacheSession(2, null, false));

        Assert.Equal(new CacheSession(2, null, false), Store().Load(Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"requests\": 3}")]
    [InlineData("{\"requests\": \"3\", \"renewalSeen\": true}")]
    [InlineData("{\"requests\": 3, \"renewalSeen\": true, \"lastResponseAt\": 999999999999999}")]
    public void Load_reads_a_damaged_file_as_no_memory(string contents)
    {
        temp.WriteFile(Path.Combine("statusline", Id + ".json"), contents);

        Assert.Null(Store().Load(Id));
    }

    [Theory]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("C:\\x")]
    public void A_session_id_that_is_not_a_plain_token_is_never_used_as_a_path(string sessionId)
    {
        var outside = temp.WriteFile("x.json", "{\"requests\": 1, \"renewalSeen\": true}");
        var store = Store();

        store.Save(sessionId, new CacheSession(1, T, true));
        store.Delete(sessionId);

        Assert.Null(store.Load(sessionId));
        Assert.True(File.Exists(outside));
        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void A_session_id_of_200_characters_is_never_used_as_a_path()
    {
        var store = Store();

        store.Save(new string('a', 200), new CacheSession(1, T, true));

        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void Delete_removes_the_session_and_ignores_a_missing_one()
    {
        var store = Store();
        store.Save(Id, new CacheSession(1, T, true));

        store.Delete(Id);
        store.Delete(Id);

        Assert.Null(store.Load(Id));
        Assert.Empty(Directory.GetFiles(Folder));
    }

    [Fact]
    public void Nothing_throws_when_the_folder_cannot_be_created()
    {
        temp.WriteFile("statusline", "a file where the folder should be");
        var store = Store();

        store.Save(Id, new CacheSession(1, T, true));
        store.Delete(Id);
        store.SweepOlderThan(T);

        Assert.Null(store.Load(Id));
    }

    [Fact]
    public void Nothing_throws_while_another_program_keeps_the_file_locked()
    {
        var store = Store();
        store.Save(Id, new CacheSession(1, T, false));
        var path = Path.Combine(Folder, Id + ".json");
        File.SetLastWriteTimeUtc(path, T.UtcDateTime.AddHours(-3));

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Save(Id, new CacheSession(2, T, false));
            store.Delete(Id);
            store.SweepOlderThan(T);
            Assert.Null(store.Load(Id));
        }

        Assert.Equal(new CacheSession(1, T, false), store.Load(Id));
        Assert.Single(Directory.GetFiles(Folder));
    }

    [Fact]
    public void SweepOlderThan_deletes_only_json_files_in_the_folder_written_before_the_cutoff()
    {
        var old = T.UtcDateTime.AddHours(-2).AddSeconds(-1);
        var stale = Touch("stale.json", old);
        var upper = Touch("UPPER.JSON", old);
        var fresh = Touch("fresh.json", T.UtcDateTime.AddHours(-2));
        var text = Touch("notes.txt", old);
        var longer = Touch("other.jsonx", old);
        var temporary = Touch("s.0123.tmp", old);
        var nested = Touch(Path.Combine("sub.json", "inner.json"), old);
        Directory.SetLastWriteTimeUtc(Path.Combine(Folder, "sub.json"), old);
        var outside = temp.WriteFile("outside.json");
        File.SetLastWriteTimeUtc(outside, old);

        Store().SweepOlderThan(T.AddHours(-2));

        Assert.False(File.Exists(stale));
        Assert.False(File.Exists(upper));
        Assert.All([fresh, text, longer, temporary, nested, outside], path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void SweepOlderThan_ignores_a_missing_folder()
    {
        Store().SweepOlderThan(T);

        Assert.False(Directory.Exists(Folder));
    }

    [Fact]
    public void DefaultDirectory_is_the_statusline_folder_under_the_user_profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, ".nexer-ai", "statusline"), FileSessionCacheStore.DefaultDirectory);
    }

    [Fact]
    public void The_folder_must_be_an_absolute_path()
    {
        Assert.Throws<ArgumentException>(() => new FileSessionCacheStore("statusline"));
    }

    private FileSessionCacheStore Store() => new(Folder);

    private string Touch(string relativePath, DateTime lastWriteUtc)
    {
        var path = temp.WriteFile(Path.Combine("statusline", relativePath));
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
