using NexerAI.Adapters.Prompts;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Tests.Prompts;

/// <summary>Failures around the prompts that must never change what the person decided.</summary>
public sealed class ConsolePrompterRobustnessTests
{
    private const string Prompt = "Azure DevOps token for dev.azure.com/acme";

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    public void A_drain_that_fails_does_not_lose_a_completed_secret(Type exceptionType)
    {
        var console = new FakeConsole { KeyAvailableException = (Exception)Activator.CreateInstance(exceptionType, "No key state.")! };
        console.Type("token\r");

        Assert.Equal("token", new ConsolePrompter(console).ReadSecret(Prompt));
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    public void A_drain_that_fails_does_not_turn_a_cancel_into_unavailable(Type exceptionType)
    {
        var console = new FakeConsole { KeyAvailableException = (Exception)Activator.CreateInstance(exceptionType, "No key state.")! };
        console.Type("half").Press(ConsoleKey.Escape, '\x1b');

        Assert.Throws<OperationCanceledException>(() => new ConsolePrompter(console).ReadSecret(Prompt));
    }

    [Fact]
    public void A_drain_that_fails_does_not_stop_the_other_questions()
    {
        var console = new FakeConsole { KeyAvailableException = new InvalidOperationException("No key state.") };
        console.Lines.Enqueue("y");
        console.Lines.Enqueue("2");

        var prompter = new ConsolePrompter(console);

        Assert.True(prompter.Confirm("Install nexer-mod?"));
        Assert.Equal("QA", prompter.Choose("Role?", ["Developer", "QA"]));
    }

    [Fact]
    public void An_endless_key_source_cannot_keep_the_drain_from_ending()
    {
        var console = new FakeConsole { EndlessKeys = true };
        console.Type("token\r");

        Assert.Equal("token", new ConsolePrompter(console).ReadSecret(Prompt));
        Assert.InRange(console.EndlessReads, 1, ConsolePrompter.MaxDiscardedKeys);
    }

    [Fact]
    public void An_endless_key_source_cannot_keep_a_cancel_from_ending()
    {
        var console = new FakeConsole { EndlessKeys = true };
        console.Press(ConsoleKey.Escape, '\x1b');

        Assert.Throws<OperationCanceledException>(() => new ConsolePrompter(console).ReadSecret(Prompt));
        Assert.InRange(console.EndlessReads, 1, ConsolePrompter.MaxDiscardedKeys);
    }
}
