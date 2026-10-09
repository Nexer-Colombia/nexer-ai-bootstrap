namespace NexerAI.Adapters.Tests;

/// <summary>A fresh directory under the system temp folder, deleted with everything in it on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory() => Path = Directory.CreateTempSubdirectory("nexer-ai-tests-").FullName;

    public string Path { get; }

    /// <summary>Writes <paramref name="contents"/> to a file at <paramref name="relativePath"/>, creating its folders.</summary>
    public string WriteFile(string relativePath, string contents = "")
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
