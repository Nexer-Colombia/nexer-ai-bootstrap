using System.Text;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.FileSystem;

/// <summary>Reads the local file system. Files that are missing or cannot be read read as <see langword="null"/>.</summary>
public sealed class LocalFileSystem : IReadOnlyFileSystem
{
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public string? ReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // Missing, a folder, locked or denied.
        }
    }
}
