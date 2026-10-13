namespace NexerAI.Core.Ports;

/// <summary>
/// Read-only view of the local file system, for logic that inspects files without changing them,
/// such as finding the task key of a working directory. Paths are absolute and may use forward slashes.
/// </summary>
public interface IReadOnlyFileSystem
{
    /// <summary>Returns whether <paramref name="path"/> is an existing folder.</summary>
    bool DirectoryExists(string path);

    /// <summary>Returns whether <paramref name="path"/> is an existing file.</summary>
    bool FileExists(string path);

    /// <summary>Returns the text of the file as UTF-8, or <see langword="null"/> when it is missing or cannot be read.</summary>
    string? ReadAllText(string path);
}
