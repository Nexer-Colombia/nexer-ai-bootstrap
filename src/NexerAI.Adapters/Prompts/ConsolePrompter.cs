using System.Globalization;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Prompts;

/// <summary>
/// Asks the person at the console. Every question needs an interactive console: with redirected input it throws
/// <see cref="PromptUnavailableException"/> before writing anything, because answers for scripts and CI come from
/// the options of <c>--non-interactive</c>, not from a pipe. An invalid answer asks again; end of input (Ctrl+Z
/// then Enter) throws <see cref="OperationCanceledException"/>. Answers are never repeated in messages.
/// </summary>
public sealed class ConsolePrompter : IPrompter
{
    private readonly IConsole console;

    /// <summary>Creates a prompter on the process console.</summary>
    public ConsolePrompter()
        : this(new SystemConsole())
    {
    }

    internal ConsolePrompter(IConsole console) => this.console = console;

    /// <inheritdoc />
    /// <remarks>Accepts the number of a choice or the choice itself, ignoring case and surrounding spaces.</remarks>
    /// <exception cref="ArgumentException"><paramref name="choices"/> is empty.</exception>
    /// <exception cref="PromptUnavailableException">There is no interactive console.</exception>
    /// <exception cref="OperationCanceledException">Input ended before a valid answer.</exception>
    public string Choose(string question, IReadOnlyList<string> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count == 0)
        {
            throw new ArgumentException("A question needs at least one choice.", nameof(choices));
        }

        EnsureInteractive(question);
        console.Write(question + Environment.NewLine);
        for (var i = 0; i < choices.Count; i++)
        {
            console.Write($"  {i + 1}) {choices[i]}{Environment.NewLine}");
        }

        while (true)
        {
            var answer = Ask($"Type a number from 1 to {choices.Count}: ");
            if (int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= choices.Count)
            {
                return choices[number - 1];
            }

            foreach (var choice in choices)
            {
                if (string.Equals(choice, answer, StringComparison.OrdinalIgnoreCase))
                {
                    return choice;
                }
            }

            console.Write("That is not one of the choices." + Environment.NewLine);
        }
    }

    /// <inheritdoc />
    /// <remarks>Accepts <c>y</c>, <c>yes</c>, <c>n</c> and <c>no</c>, ignoring case; an empty answer is no.</remarks>
    /// <exception cref="PromptUnavailableException">There is no interactive console.</exception>
    /// <exception cref="OperationCanceledException">Input ended before a valid answer.</exception>
    public bool Confirm(string question)
    {
        EnsureInteractive(question);
        while (true)
        {
            switch (Ask($"{question} [y/N] ").ToUpperInvariant())
            {
                case "Y" or "YES":
                    return true;
                case "" or "N" or "NO":
                    return false;
                default:
                    console.Write("Answer y or n." + Environment.NewLine);
                    break;
            }
        }
    }

    /// <inheritdoc />
    public string ReadSecret(string prompt) => throw new NotSupportedException();

    private string Ask(string prompt)
    {
        console.Write(prompt);
        return console.ReadLine()?.Trim() ?? throw new OperationCanceledException("Input ended before an answer.");
    }

    private void EnsureInteractive(string question)
    {
        if (console.IsInputRedirected)
        {
            throw Unavailable(question);
        }
    }

    private static PromptUnavailableException Unavailable(string question, Exception? innerException = null)
    {
        var message =
            $"Cannot ask \"{question}\": nexer-ai needs an interactive console, and standard input is redirected or cannot read keys. "
            + "Run it in PowerShell or Windows Terminal, or pass the answers as options with --non-interactive.";
        return innerException is null ? new(message) : new(message, innerException);
    }
}
