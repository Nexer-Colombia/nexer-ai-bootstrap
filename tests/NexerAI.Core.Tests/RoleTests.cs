using NexerAI.Core.Domain;

namespace NexerAI.Core.Tests;

public sealed class RoleTests
{
    [Theory]
    [InlineData("developer", Role.Developer)]
    [InlineData("qa", Role.Qa)]
    [InlineData("po", Role.ProductOwner)]
    public void TryParse_maps_each_token_and_ToToken_maps_it_back(string token, Role expected)
    {
        Assert.True(RoleTokens.TryParse(token, out var role));
        Assert.Equal(expected, role);
        Assert.Equal(token, role.ToToken());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Developer")]
    [InlineData("QA")]
    [InlineData("ProductOwner")]
    [InlineData("1")]
    [InlineData(" qa")]
    public void TryParse_rejects_anything_but_an_exact_token(string? token)
    {
        Assert.False(RoleTokens.TryParse(token, out _));
    }

    [Fact]
    public void Parse_maps_a_token_and_rejects_anything_else_naming_the_tokens()
    {
        Assert.Equal(Role.Qa, RoleTokens.Parse("qa"));

        var error = Assert.Throws<ArgumentException>(() => RoleTokens.Parse("QA"));
        Assert.Contains("'QA'", error.Message, StringComparison.Ordinal);
        Assert.Contains("developer, qa, po", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void All_lists_the_tokens_in_question_order()
    {
        Assert.Equal(["developer", "qa", "po"], RoleTokens.All);
    }

    [Fact]
    public void ToToken_rejects_an_undefined_role()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((Role)42).ToToken());
    }
}
