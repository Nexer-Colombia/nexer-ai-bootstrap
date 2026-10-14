using NexerAI.Core.StatusLine;

namespace NexerAI.Core.Tests.StatusLine;

public sealed class SessionIdTests
{
    [Theory]
    [InlineData("00000000-0000-4000-8000-000000000001")]
    [InlineData("a")]
    [InlineData("A_b-9")]
    [InlineData("console")]
    [InlineData("COM10")]
    public void IsFileName_accepts_a_plain_token(string sessionId)
    {
        Assert.True(SessionId.IsFileName(sessionId));
    }

    [Fact]
    public void IsFileName_accepts_up_to_128_characters()
    {
        Assert.True(SessionId.IsFileName(new string('a', 128)));
        Assert.False(SessionId.IsFileName(new string('a', 129)));
        Assert.False(SessionId.IsFileName(new string('a', 200)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("..\\x")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("c:x")]
    [InlineData("a.b")]
    [InlineData(" a")]
    [InlineData("a\n")]
    [InlineData("a*")]
    [InlineData("é")]
    [InlineData("１")]
    public void IsFileName_rejects_anything_else(string? sessionId)
    {
        Assert.False(SessionId.IsFileName(sessionId));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("Aux")]
    [InlineData("PRN")]
    [InlineData("com1")]
    [InlineData("LPT9")]
    public void IsFileName_rejects_windows_device_names(string sessionId)
    {
        // CON.json and the like open the device, not a file.
        Assert.False(SessionId.IsFileName(sessionId));
    }
}
