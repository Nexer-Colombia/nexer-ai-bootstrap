using NexerAI.Core.Ports;

namespace NexerAI.Core.Tasks;

/// <summary>
/// Finds the task key of a working directory by reading git files directly, without starting git
/// (design 4.6). The rules are defined by the shared vectors in <c>test-vectors/task-keys.json</c>,
/// which the Nexer mod and the task skill also follow.
/// </summary>
public sealed class TaskKeyResolver(IReadOnlyFileSystem fileSystem)
{
    private const string RefPrefix = "ref:";
    private const string BranchPrefix = "refs/heads/";
    private const string GitdirPrefix = "gitdir:";

    /// <summary>The branch a reftable repository writes in its <c>HEAD</c> file for older git versions.</summary>
    private const string ReftablePlaceholder = ".invalid";

    private readonly IReadOnlyFileSystem fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <summary>
    /// Walks up from <paramref name="workingDirectory"/> to the first folder that contains <c>.git</c> and
    /// reads its branch. Returns <see langword="null"/> when there is no such folder, the HEAD is detached
    /// or not on a branch, the git files cannot be read, or the path is not an absolute drive or UNC path.
    /// </summary>
    public TaskKey? Resolve(string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);

        var start = WindowsPath.Parse(workingDirectory);
        if (start is null || GitRepository.FindRoot(fileSystem, start) is not { } root)
        {
            return null;
        }

        var dotGit = root.Combine(".git");
        if (fileSystem.DirectoryExists(dotGit.ToString()))
        {
            return ReadKey(root, dotGit);
        }

        var gitDirectory = ReadGitdir(root, dotGit);
        return gitDirectory is null ? null : ReadKey(root, gitDirectory);
    }

    /// <summary>Reads the branch from <c>HEAD</c>; only a symbolic ref under <c>refs/heads/</c> is a branch.</summary>
    private TaskKey? ReadKey(WindowsPath root, WindowsPath gitDirectory)
    {
        var head = fileSystem.ReadAllText(gitDirectory.Combine("HEAD").ToString())?.TrimEnd();
        if (head is null || !head.StartsWith(RefPrefix, StringComparison.Ordinal))
        {
            return null; // Missing, or a detached HEAD holding a commit id.
        }

        var target = head[RefPrefix.Length..].TrimStart();
        if (!target.StartsWith(BranchPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var branch = target[BranchPrefix.Length..];
        return branch.Length == 0 || branch == ReftablePlaceholder
            ? null
            : new TaskKey(root.ToString().ToLowerInvariant(), branch);
    }

    /// <summary>Reads the <c>gitdir:</c> line of a <c>.git</c> file; a relative path starts at <paramref name="root"/>.</summary>
    private WindowsPath? ReadGitdir(WindowsPath root, WindowsPath dotGitFile)
    {
        var text = fileSystem.ReadAllText(dotGitFile.ToString())?.Trim();
        if (text is null || !text.StartsWith(GitdirPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var target = text[GitdirPrefix.Length..].Trim();
        return target.Length == 0 ? null : WindowsPath.Parse(target) ?? WindowsPath.Parse($"{root}/{target}");
    }
}
