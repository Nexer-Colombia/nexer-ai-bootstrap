using NexerAI.Adapters.Prompts;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.Prompts;

public sealed class ConsolePrompterQuestionTests
{
    private static readonly string[] Roles = ["Developer", "QA", "PM/PO"];

    private readonly FakeConsole console = new();

    private ConsolePrompter Prompter => new(console);

    [Fact]
    public void Choose_lists_the_choices_by_number_and_returns_the_numbered_one()
    {
        console.Lines.Enqueue(" 2 ");

        Assert.Equal("QA", Prompter.Choose("What is your role?", Roles));
        Assert.Equal(
            Lines("What is your role?", "  1) Developer", "  2) QA", "  3) PM/PO") + "Type a number from 1 to 3: ",
            console.Output);
    }

    [Fact]
    public void Choose_accepts_a_choice_typed_in_full_ignoring_case()
    {
        console.Lines.Enqueue("pm/po");

        Assert.Equal("PM/PO", Prompter.Choose("What is your role?", Roles));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("-1")]
    [InlineData("tester")]
    public void Choose_asks_again_after_an_answer_that_is_not_a_choice(string invalid)
    {
        console.Lines.Enqueue(invalid);
        console.Lines.Enqueue("1");

        Assert.Equal("Developer", Prompter.Choose("What is your role?", Roles));
        Assert.EndsWith(
            "Type a number from 1 to 3: " + Lines("That is not one of the choices.") + "Type a number from 1 to 3: ",
            console.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Choose_without_choices_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Prompter.Choose("What is your role?", []));
        Assert.Empty(console.Output);
    }

    [Theory]
    [InlineData("y", true)]
    [InlineData(" YES ", true)]
    [InlineData("n", false)]
    [InlineData("No", false)]
    [InlineData("", false)]
    public void Confirm_reads_yes_or_no_and_an_empty_answer_is_no(string answer, bool expected)
    {
        console.Lines.Enqueue(answer);

        Assert.Equal(expected, Prompter.Confirm("Install nexer-mod?"));
        Assert.Equal("Install nexer-mod? [y/N] ", console.Output);
    }

    [Fact]
    public void Confirm_asks_again_after_another_answer()
    {
        console.Lines.Enqueue("si");
        console.Lines.Enqueue("y");

        Assert.True(Prompter.Confirm("Install nexer-mod?"));
        Assert.Equal("Install nexer-mod? [y/N] " + Lines("Answer y or n.") + "Install nexer-mod? [y/N] ", console.Output);
    }

    [Fact]
    public void End_of_input_cancels_instead_of_asking_forever()
    {
        console.Lines.Enqueue("tester");
        console.Lines.Enqueue(null);
        console.Lines.Enqueue(null);

        Assert.Throws<OperationCanceledException>(() => Prompter.Choose("What is your role?", Roles));
        Assert.Throws<OperationCanceledException>(() => Prompter.Confirm("Install nexer-mod?"));
    }

    [Fact]
    public void Redirected_input_is_refused_before_anything_is_written_or_read()
    {
        var redirected = new FakeConsole { IsInputRedirected = true };
        var prompter = new ConsolePrompter(redirected);

        var choose = Assert.Throws<PromptUnavailableException>(() => prompter.Choose("What is your role?", Roles));
        Assert.Throws<PromptUnavailableException>(() => prompter.Confirm("Install nexer-mod?"));

        Assert.Contains("What is your role?", choose.Message, StringComparison.Ordinal);
        Assert.Contains("--non-interactive", choose.Message, StringComparison.Ordinal);
        Assert.Empty(redirected.Output);
    }

    private static string Lines(params string[] lines) => string.Concat(lines.Select(line => line + Environment.NewLine));
}
