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
    /// <remarks>
    /// Nothing is echoed, not even a <c>*</c> per character, so a screen share or recording shows neither the secret
    /// nor its length. Backspace removes the last character (a surrogate pair as one), Enter ends the input, Escape
    /// or Ctrl+C cancels; arrows, function keys and other control keys are ignored. Keys left after Enter or a
    /// cancel, such as the rest of a pasted clipboard, are discarded so the next question cannot echo them. An
    /// empty or blank answer asks again. The characters are collected in an array that is cleared afterwards; the
    /// returned string cannot be wiped.
    /// </remarks>
    /// <exception cref="PromptUnavailableException">There is no interactive console; the message never holds what was typed.</exception>
    /// <exception cref="OperationCanceledException">The person pressed Escape or Ctrl+C.</exception>
    public string ReadSecret(string prompt)
    {
        EnsureInteractive(prompt);
        bool? controlCAsInput = null;
        try
        {
            // Ctrl+C would otherwise end the process with the cursor left on the prompt line.
            controlCAsInput = console.TreatControlCAsInput;
            console.TreatControlCAsInput = true;
            while (true)
            {
                console.Write($"{prompt} (input hidden; Esc cancels): ");
                string? secret;
                try
                {
                    secret = ReadHidden();
                }
                finally
                {
                    console.Write(Environment.NewLine);
                }

                if (secret is not null)
                {
                    return secret;
                }

                console.Write("Nothing was entered. Type or paste the value, or press Esc to cancel." + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            throw Unavailable(prompt, ex);
        }
        finally
        {
            if (controlCAsInput is { } previous)
            {
                Restore(previous);
            }
        }
    }

    /// <summary>Reads keys until Enter; returns <see langword="null"/> for a blank answer.</summary>
    private string? ReadHidden()
    {
        var buffer = new char[64];
        var length = 0;
        try
        {
            while (true)
            {
                var key = console.ReadKey();
                if (key.Key == ConsoleKey.Enter || key.KeyChar is '\r' or '\n')
                {
                    DiscardPendingKeys();
                    return buffer.AsSpan(0, length).IsWhiteSpace() ? null : new string(buffer, 0, length);
                }

                if (key.Key == ConsoleKey.Escape || key.KeyChar == '\x03')
                {
                    DiscardPendingKeys();
                    throw new OperationCanceledException("The secret prompt was cancelled.");
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    var remove = length >= 2 && char.IsSurrogatePair(buffer[length - 2], buffer[length - 1]) ? 2 : Math.Min(length, 1);
                    length -= remove;
                    Array.Clear(buffer, length, remove);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    if (length == buffer.Length)
                    {
                        var larger = new char[buffer.Length * 2];
                        buffer.CopyTo(larger, 0);
                        Array.Clear(buffer);
                        buffer = larger;
                    }

                    buffer[length++] = key.KeyChar;
                }
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private void DiscardPendingKeys()
    {
        while (console.KeyAvailable)
        {
            console.ReadKey();
        }
    }

    private void Restore(bool controlCAsInput)
    {
        try
        {
            console.TreatControlCAsInput = controlCAsInput;
        }
        catch (IOException)
        {
            // The console went away; there is nothing left to restore.
        }
    }

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
