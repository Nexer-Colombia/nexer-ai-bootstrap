using NexerAI.Adapters.FileSystem;
using NexerAI.Core.Tasks;

namespace NexerAI.Adapters.Tests.FileSystem;

public sealed class LocalFileSystemTests : IDisposable
{
    private readonly TempDirectory root = new();

    private readonly LocalFileSystem fileSystem = new();

    public void Dispose() => root.Dispose();

    private string RootKey => root.Path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    [Fact]
    public void Reports_files_and_folders_with_either_separator()
    {
        var head = root.WriteFile(@"acme\.git\HEAD", "ref: refs/heads/main\n");
        var gitFolder = Path.GetDirectoryName(head)!;

        Assert.True(fileSystem.DirectoryExists(gitFolder));
        Assert.True(fileSystem.DirectoryExists(gitFolder.Replace('\\', '/')));
        Assert.False(fileSystem.DirectoryExists(head));
        Assert.True(fileSystem.FileExists(head.Replace('\\', '/')));
        Assert.False(fileSystem.FileExists(gitFolder));
    }

    [Fact]
    public void ReadAllText_reads_utf8()
    {
        var head = root.WriteFile("HEAD", "ref: refs/heads/feature/añadir-ñandú\n");

        Assert.Equal("ref: refs/heads/feature/añadir-ñandú\n", fileSystem.ReadAllText(head));
    }

    [Fact]
    public void ReadAllText_returns_null_for_a_missing_file_or_a_folder()
    {
        Assert.Null(fileSystem.ReadAllText(Path.Combine(root.Path, "missing", "HEAD")));
        Assert.Null(fileSystem.ReadAllText(root.Path));
    }

    [Fact]
    public void Resolver_finds_the_branch_from_a_subfolder()
    {
        root.WriteFile(@"Acme\.git\HEAD", "ref: refs/heads/feature/12345-header\r\n");
        var cwd = Path.GetDirectoryName(root.WriteFile(@"Acme\src\Web\Program.cs"))!;

        var key = new TaskKeyResolver(fileSystem).Resolve(cwd);

        Assert.Equal($"{RootKey}/acme#feature/12345-header", key?.Value);
    }

    [Fact]
    public void Resolver_follows_a_worktree_git_file_with_an_absolute_gitdir()
    {
        var worktreeGitDir = Path.GetDirectoryName(root.WriteFile(@"acme\.git\worktrees\acme-header\HEAD", "ref: refs/heads/feature/12345-header\n"))!;
        root.WriteFile(@"acme\.git\HEAD", "ref: refs/heads/main\n");
        root.WriteFile(@"acme-header\.git", $"gitdir: {worktreeGitDir.Replace('\\', '/')}\n");

        var key = new TaskKeyResolver(fileSystem).Resolve(Path.Combine(root.Path, "acme-header"));

        Assert.Equal($"{RootKey}/acme-header#feature/12345-header", key?.Value);
    }

    [Fact]
    public void Resolver_follows_a_relative_gitdir_from_the_folder_of_the_git_file()
    {
        root.WriteFile(@"acme\.git\HEAD", "ref: refs/heads/develop\n");
        root.WriteFile(@"acme\.git\modules\libs\shared\HEAD", "ref: refs/heads/main\n");
        root.WriteFile(@"acme\libs\shared\.git", "gitdir: ../../.git/modules/libs/shared\n");

        var key = new TaskKeyResolver(fileSystem).Resolve(Path.Combine(root.Path, "acme", "libs", "shared"));

        Assert.Equal($"{RootKey}/acme/libs/shared#main", key?.Value);
    }

    [Fact]
    public void Resolver_returns_null_for_a_detached_head()
    {
        root.WriteFile(@"acme\.git\HEAD", "3f786850e387550fdab836ed7e6dc881de23001b\n");

        Assert.Null(new TaskKeyResolver(fileSystem).Resolve(Path.Combine(root.Path, "acme")));
    }
}
