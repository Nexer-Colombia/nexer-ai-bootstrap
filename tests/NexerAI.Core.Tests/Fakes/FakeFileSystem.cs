using NexerAI.Core.Ports;

namespace NexerAI.Core.Tests.Fakes;

/// <summary>
/// In-memory file tree with Windows semantics: paths match case-insensitively, either separator works,
/// and every parent folder of a file exists.
/// </summary>
public sealed class FakeFileSystem : IReadOnlyFileSystem
{
    private readonly Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);

    public FakeFileSystem(IReadOnlyDictionary<string, string>? files = null)
    {
        foreach (var (path, contents) in files ?? new Dictionary<string, string>())
        {
            this.files[Normalize(path)] = contents;
        }
    }

    public List<string> ReadPaths { get; } = [];

    public bool DirectoryExists(string path)
    {
        var prefix = Normalize(path).TrimEnd('/') + "/";
        return files.Keys.Any(file => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public bool FileExists(string path) => files.ContainsKey(Normalize(path));

    public string? ReadAllText(string path)
    {
        ReadPaths.Add(path);
        return files.GetValueOrDefault(Normalize(path));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
