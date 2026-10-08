using System.Text.Json;
using NexerAI.Core.Remotes;

namespace NexerAI.Core.Tests.Remotes;

public sealed class RemoteNormalizerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly Lazy<VectorFile> Vectors = new(() =>
        JsonSerializer.Deserialize<VectorFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", "remotes.json")),
            JsonOptions)
        ?? throw new InvalidOperationException("test-vectors/remotes.json is empty."));

    /// <summary>One row per case in the shared vector file: adding a vector adds a test.</summary>
    public static TheoryData<string, string?> Cases()
    {
        var data = new TheoryData<string, string?>();
        foreach (var vector in Vectors.Value.Cases)
        {
            data.Add(vector.Input, vector.Expected);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TryNormalize_matches_the_shared_vectors(string input, string? expected)
    {
        var accepted = RemoteNormalizer.TryNormalize(input, out var key);

        Assert.Equal(expected is not null, accepted);
        Assert.Equal(expected, key);
    }

    [Fact]
    public void Vector_file_is_well_formed()
    {
        var file = Vectors.Value;

        Assert.Equal(1, file.Schema);
        Assert.False(string.IsNullOrWhiteSpace(file.Description));
        Assert.NotEmpty(file.Cases);
        Assert.All(file.Cases, vector => Assert.NotNull(vector.Input));
        Assert.Equal(file.Cases.Count, file.Cases.Select(v => v.Input).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TryNormalize_rejects_null()
    {
        Assert.False(RemoteNormalizer.TryNormalize(null, out var key));
        Assert.Null(key);
    }

    [Fact]
    public void Normalize_returns_the_key()
    {
        Assert.Equal("github.com/acme/website", RemoteNormalizer.Normalize("git@github.com:Acme/website.git"));
    }

    [Fact]
    public void Normalize_throws_on_rejected_input()
    {
        var error = Assert.Throws<ArgumentException>(() => RemoteNormalizer.Normalize(@"C:\repos\website"));

        Assert.Contains(@"C:\repos\website", error.Message, StringComparison.Ordinal);
    }

    private sealed record VectorFile(int Schema, string Description, IReadOnlyList<Vector> Cases);

    private sealed record Vector(string Input, string? Expected, string? Note);
}
