using NexerAI.Core.Ports;

namespace NexerAI.Core.Tasks;

/// <summary>Finds the root of the git working copy around a folder by reading the file system, without starting git.</summary>
internal static class GitRepository
{
    /// <summary>
    /// Walks up from <paramref name="start"/> to the first folder that contains <c>.git</c>: a folder with a
    /// <c>HEAD</c> file (like git, a <c>.git</c> folder without one is skipped) or a file (worktree, submodule).
    /// Returns <see langword="null"/> when there is none.
    /// </summary>
    public static WindowsPath? FindRoot(IReadOnlyFileSystem fileSystem, WindowsPath start)
    {
        for (var directory = start; directory is not null; directory = directory.Parent)
        {
            var dotGit = directory.Combine(".git");
            if (fileSystem.DirectoryExists(dotGit.ToString())
                ? fileSystem.FileExists(dotGit.Combine("HEAD").ToString())
                : fileSystem.FileExists(dotGit.ToString()))
            {
                return directory;
            }
        }

        return null;
    }
}
