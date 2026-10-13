using NexerAI.Core.Tasks;

namespace NexerAI.Core.Tests.Tasks;

public sealed class TaskKeyTests
{
    public static TheoryData<string> CasesWithAKey() => TaskKeyVectors.Names(v => v.Expected is not null);

    [Fact]
    public void Vector_file_is_well_formed()
    {
        var file = TaskKeyVectors.Contents;

        Assert.Equal(1, file.Schema);
        Assert.False(string.IsNullOrWhiteSpace(file.Description));
        Assert.NotEmpty(file.Cases);
        Assert.All(file.Cases, vector =>
        {
            Assert.False(string.IsNullOrWhiteSpace(vector.Name));
            Assert.False(string.IsNullOrWhiteSpace(vector.Cwd));
            Assert.NotEmpty(vector.Files);
        });
        Assert.Equal(file.Cases.Count, file.Cases.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(CasesWithAKey))]
    public void Sha256_matches_the_shared_vectors(string name)
    {
        var expected = TaskKeyVectors.Get(name).Expected!;
        var separator = expected.Key.IndexOf('#', StringComparison.Ordinal);

        var key = new TaskKey(expected.Key[..separator], expected.Key[(separator + 1)..]);

        Assert.Equal(expected.Key, key.Value);
        Assert.Equal(expected.Sha256, key.Sha256);
    }

    [Fact]
    public void Value_joins_root_and_branch_and_Sha256_hashes_the_utf8_key()
    {
        var key = new TaskKey("c:/work/acme", "main");

        Assert.Equal("c:/work/acme#main", key.Value);
        Assert.Equal(key.Value, key.ToString());
        Assert.Equal("5f3c373f73d7eb8647cf654d4162aeb51864f5d0b5c43777843f745b4f16bbe5", key.Sha256);
    }
}
