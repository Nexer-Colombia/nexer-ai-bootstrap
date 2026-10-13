using NexerAI.Core.Tasks;
using NexerAI.Core.Tests.Fakes;

namespace NexerAI.Core.Tests.Tasks;

public sealed class TaskKeyResolverTests
{
    public static TheoryData<string> Cases() => TaskKeyVectors.Names();

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_matches_the_shared_vectors(string name)
    {
        var vector = TaskKeyVectors.Get(name);
        var resolver = new TaskKeyResolver(new FakeFileSystem(vector.Files));

        var key = resolver.Resolve(vector.Cwd);

        Assert.Equal(vector.Expected?.Key, key?.Value);
        Assert.Equal(vector.Expected?.Sha256, key?.Sha256);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"work\acme")]
    [InlineData("C:")]
    [InlineData(@"C:work\acme")]
    [InlineData(@"\\server")]
    public void Resolve_returns_null_for_a_working_directory_that_is_not_an_absolute_path(string cwd)
    {
        var resolver = new TaskKeyResolver(new FakeFileSystem(new Dictionary<string, string>
        {
            ["c:/work/acme/.git/HEAD"] = "ref: refs/heads/main\n",
        }));

        Assert.Null(resolver.Resolve(cwd));
    }

    [Fact]
    public void Resolve_resolves_dot_segments_in_the_working_directory_and_in_gitdir()
    {
        var resolver = new TaskKeyResolver(new FakeFileSystem(new Dictionary<string, string>
        {
            ["c:/work/acme/.git/worktrees/web/HEAD"] = "ref: refs/heads/main\n",
            ["c:/work/web/.git"] = "gitdir: ./../acme/./.git/worktrees/web\n",
        }));

        Assert.Equal("c:/work/web#main", resolver.Resolve(@"C:\work\web\src\.\..")?.Value);
    }

    [Fact]
    public void Resolve_keeps_the_path_case_for_file_access_and_reads_only_head()
    {
        var files = new FakeFileSystem(new Dictionary<string, string> { ["c:/work/acme/.git/HEAD"] = "ref: refs/heads/main\n" });

        new TaskKeyResolver(files).Resolve(@"C:\Work\Acme\src");

        Assert.Equal(["C:/Work/Acme/.git/HEAD"], files.ReadPaths);
    }

    [Fact]
    public void Resolve_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => new TaskKeyResolver(null!));
        Assert.Throws<ArgumentNullException>(() => new TaskKeyResolver(new FakeFileSystem()).Resolve(null!));
    }
}
