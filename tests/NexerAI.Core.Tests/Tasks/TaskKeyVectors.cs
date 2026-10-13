using System.Text.Json;

namespace NexerAI.Core.Tests.Tasks;

/// <summary>The shared vectors in <c>test-vectors/task-keys.json</c>, the contract with the Nexer mod and the task skill.</summary>
internal static class TaskKeyVectors
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly Lazy<VectorFile> Vectors = new(() =>
        JsonSerializer.Deserialize<VectorFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", "task-keys.json")),
            JsonOptions)
        ?? throw new InvalidOperationException("test-vectors/task-keys.json is empty."));

    public static VectorFile Contents => Vectors.Value;

    public static Vector Get(string name) => Vectors.Value.Cases.Single(v => v.Name == name);

    /// <summary>One row per case name, optionally filtered: adding a vector adds a test.</summary>
    public static TheoryData<string> Names(Func<Vector, bool>? filter = null)
    {
        var data = new TheoryData<string>();
        foreach (var vector in Vectors.Value.Cases.Where(filter ?? (_ => true)))
        {
            data.Add(vector.Name);
        }

        return data;
    }

    internal sealed record VectorFile(int Schema, string Description, IReadOnlyList<Vector> Cases);

    internal sealed record Vector(string Name, Dictionary<string, string> Files, string Cwd, ExpectedKey? Expected);

    internal sealed record ExpectedKey(string Key, string Sha256);
}
