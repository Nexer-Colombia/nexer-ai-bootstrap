using NexerAI.Adapters.Prompts;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.Prompts;

public sealed class ConsolePrompterSecretTests
{
    private const string Prompt = "Azure DevOps token for dev.azure.com/acme";

    private const string Shown = Prompt + " (input hidden; Esc cancels): ";

    private const string Secret = "pat-Ünïcødé-😀-7f3a";

    private readonly FakeConsole console = new();

    private ConsolePrompter Prompter => new(console);

    [Fact]
    public void A_typed_secret_is_returned_and_never_written()
    {
        console.Type(Secret + "\r");

        Assert.Equal(Secret, Prompter.ReadSecret(Prompt));
        Assert.Equal(Shown + Environment.NewLine, console.Output);
        Assert.Equal([true, false], console.ControlCSettings);
    }

    [Theory]
    [InlineData("abx\b\bc\r", "ac")]
    [InlineData("\b\ba\r", "a")]
    [InlineData("a😀\bb\r", "ab")]
    [InlineData("ok\n", "ok")]
    public void Backspace_removes_the_last_character_and_a_line_feed_also_ends_the_input(string keys, string expected)
    {
        console.Type(keys);

        Assert.Equal(expected, Prompter.ReadSecret(Prompt));
    }

    [Fact]
    public void Arrows_function_keys_tab_and_other_control_keys_are_ignored()
    {
        console.Type("o")
            .Press(ConsoleKey.LeftArrow)
            .Press(ConsoleKey.Home)
            .Press(ConsoleKey.F5)
            .Press(ConsoleKey.Tab, '\t')
            .Press(ConsoleKey.V, '\x16', control: true)
            .Type("k\r");

        Assert.Equal("ok", Prompter.ReadSecret(Prompt));
    }

    [Fact]
    public void A_pasted_secret_with_a_line_end_is_read_and_the_keys_after_it_are_discarded()
    {
        console.Type("token\r\nnext line of the clipboard");

        Assert.Equal("token", Prompter.ReadSecret(Prompt));
        Assert.Empty(console.Keys);
    }

    [Fact]
    public void A_long_secret_is_returned_intact()
    {
        var secret = string.Concat(Enumerable.Repeat("0123456789", 100));
        console.Type(secret + "\r");

        Assert.Equal(secret, Prompter.ReadSecret(Prompt));
    }

    [Fact]
    public void An_empty_or_blank_answer_asks_again()
    {
        console.Type("\r").Pause().Type("   \r").Pause().Type("token\r");

        Assert.Equal("token", Prompter.ReadSecret(Prompt));
        var again = Environment.NewLine + "Nothing was entered. Type or paste the value, or press Esc to cancel." + Environment.NewLine;
        Assert.Equal(Shown + again + Shown + again + Shown + Environment.NewLine, console.Output);
    }

    [Theory]
    [InlineData(ConsoleKey.Escape, '\x1b')]
    [InlineData(ConsoleKey.C, '\x03')]
    public void Escape_or_ctrl_c_cancels_and_discards_what_was_typed(ConsoleKey key, char keyChar)
    {
        console.Type("half-typed").Press(key, keyChar, control: key == ConsoleKey.C).Type("more keys");

        var ex = Assert.Throws<OperationCanceledException>(() => Prompter.ReadSecret(Prompt));

        Assert.DoesNotContain("half", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Shown + Environment.NewLine, console.Output);
        Assert.Empty(console.Keys);
        Assert.Equal([true, false], console.ControlCSettings);
    }

    [Fact]
    public void Redirected_input_is_refused_before_anything_is_written_or_read()
    {
        var redirected = new FakeConsole { IsInputRedirected = true };
        redirected.Type("token\r");

        var ex = Assert.Throws<PromptUnavailableException>(() => new ConsolePrompter(redirected).ReadSecret(Prompt));

        Assert.Contains(Prompt, ex.Message, StringComparison.Ordinal);
        Assert.Empty(redirected.Output);
        Assert.Equal(6, redirected.Keys.Count);
        Assert.Empty(redirected.ControlCSettings);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    public void A_console_that_cannot_read_keys_is_refused_without_the_partial_secret(Type exceptionType)
    {
        var broken = new FakeConsole { ReadKeyException = (Exception)Activator.CreateInstance(exceptionType, "Cannot read keys.")! };
        broken.Type("partial");

        var ex = Assert.Throws<PromptUnavailableException>(() => new ConsolePrompter(broken).ReadSecret(Prompt));

        Assert.IsType(exceptionType, ex.InnerException);
        Assert.Contains("--non-interactive", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("partial", ex.Message, StringComparison.Ordinal);
        Assert.Equal([true, false], broken.ControlCSettings);
    }

    [Fact]
    public void Characters_typed_with_altgr_are_kept()
    {
        // AltGr arrives as Control+Alt with the character in KeyChar: @ on a Spanish layout is AltGr+2.
        const string AltGrCharacters = "@€[]{}\\|#~";
        console.Type("a").TypeAltGr(AltGrCharacters).Type("z\r");

        Assert.Equal("a" + AltGrCharacters + "z", Prompter.ReadSecret(Prompt));
    }
}
