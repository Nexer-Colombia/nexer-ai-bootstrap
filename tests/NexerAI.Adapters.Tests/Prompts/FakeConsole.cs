using System.Text;
using NexerAI.Adapters.Prompts;

namespace NexerAI.Adapters.Tests.Prompts;

/// <summary>
/// Replays scripted keys and lines and records what was written. Running out of script fails the test with
/// <see cref="InvalidDataException"/>, which the prompter does not catch.
/// </summary>
internal sealed class FakeConsole : IConsole
{
    private readonly StringBuilder output = new();

    private readonly StringBuilder standardOutput = new();

    private readonly List<(int Checks, ConsoleKeyInfo Key)> late = [];

    private bool treatControlCAsInput;

    private int checks;

    public bool IsInputRedirected { get; init; }

    public bool IsOutputRedirected { get; init; }

    public bool IsErrorRedirected { get; init; }

    /// <summary>Thrown by <see cref="ReadKey"/> once the scripted keys run out, like a console that cannot read keys.</summary>
    public Exception? ReadKeyException { get; init; }

    /// <summary>Thrown by <see cref="KeyAvailable"/>, like a console that cannot tell whether a key is waiting.</summary>
    public Exception? KeyAvailableException { get; init; }

    /// <summary>Thrown when restoring <see cref="TreatControlCAsInput"/> to <see langword="false"/>.</summary>
    public Exception? RestoreException { get; init; }

    /// <summary>Thrown by <see cref="Write"/> when the text equals <see cref="FailingWrite"/>.</summary>
    public Exception? WriteException { get; init; }

    /// <summary>The only text <see cref="WriteException"/> applies to.</summary>
    public string? FailingWrite { get; init; }

    /// <summary>Once the first text is written, an endless source of <c>x</c> keys after the scripted ones, like a stuck key repeat.</summary>
    public bool EndlessKeys { get; init; }

    /// <summary>How many keys <see cref="ReadKey"/> returned from the endless source.</summary>
    public int EndlessReads { get; private set; }

    /// <summary>
    /// Scripted keys, visible once the first text is written (the person types after seeing the question);
    /// <see langword="null"/> is a pause, after which the next keys are not yet available.
    /// </summary>
    public Queue<ConsoleKeyInfo?> Keys { get; } = new();

    /// <summary>Keys already waiting before the first question, like a stale newline; always visible.</summary>
    public Queue<ConsoleKeyInfo> Stale { get; } = new();

    /// <summary>Lines for <see cref="ReadLine"/>; <see langword="null"/> is end of input.</summary>
    public Queue<string?> Lines { get; } = new();

    public bool TreatControlCAsInput
    {
        get => treatControlCAsInput;
        set
        {
            ControlCSettings.Add(value);
            if (!value && RestoreException is not null)
            {
                throw RestoreException;
            }

            treatControlCAsInput = value;
        }
    }

    /// <summary>Every value given to <see cref="TreatControlCAsInput"/>, in order.</summary>
    public List<bool> ControlCSettings { get; } = [];

    /// <summary>Everything written, from either stream.</summary>
    public string Output => output.ToString();

    /// <summary>What was written with <see cref="WriteOutput"/>, that is, to standard output.</summary>
    public string StandardOutput => standardOutput.ToString();

    public bool KeyAvailable
    {
        get
        {
            if (KeyAvailableException is not null)
            {
                throw KeyAvailableException;
            }

            checks++;
            foreach (var arrival in late.Where(l => l.Checks < checks).ToList())
            {
                Keys.Enqueue(arrival.Key);
                late.Remove(arrival);
            }

            return Stale.Count > 0
                || (output.Length > 0 && (EndlessKeys || (Keys.TryPeek(out var key) && key is not null)));
        }
    }

    public ConsoleKeyInfo ReadKey()
    {
        if (Stale.TryDequeue(out var waiting))
        {
            return waiting;
        }

        while (Keys.TryDequeue(out var key))
        {
            if (key is { } pressed)
            {
                return pressed;
            }
        }

        if (EndlessKeys && output.Length > 0)
        {
            EndlessReads++;
            return Key(ConsoleKey.X, 'x');
        }

        throw ReadKeyException ?? new InvalidDataException("No scripted key left.");
    }

    /// <summary>A waiting newline ends the line at once, the way a real console hands over stale input.</summary>
    public string? ReadLine()
    {
        if (Stale.Count > 0 || (output.Length > 0 && Keys.TryPeek(out var head) && head is not null))
        {
            var typed = new StringBuilder();
            while (ReadKey() is var key && key.KeyChar is not ('\r' or '\n'))
            {
                typed.Append(key.KeyChar);
            }

            return typed.ToString();
        }

        return Lines.TryDequeue(out var line) ? line : throw new InvalidDataException("No scripted line left.");
    }

    public void Write(string text) => Append(text, toStandardOutput: false);

    public void WriteOutput(string text) => Append(text, toStandardOutput: true);

    /// <summary>Queues one key per UTF-16 unit of <paramref name="text"/>, as typing or a paste arrives.</summary>
    public FakeConsole Type(string text)
    {
        foreach (var c in text)
        {
            Keys.Enqueue(Letter(c));
        }

        return this;
    }

    /// <summary>Queues keys that exist before the first question is written.</summary>
    public FakeConsole TypeBefore(string text)
    {
        foreach (var c in text)
        {
            Stale.Enqueue(Letter(c));
        }

        return this;
    }

    /// <summary>
    /// Keys that appear only after <paramref name="afterChecks"/> calls of <see cref="KeyAvailable"/>, like the end
    /// of a paste that reaches the console after the prompter looked.
    /// </summary>
    public FakeConsole Late(int afterChecks, string text)
    {
        foreach (var c in text)
        {
            late.Add((afterChecks, Letter(c)));
        }

        return this;
    }

    /// <summary>Queues each character as typed with Control and Alt held, which is how AltGr arrives on Windows.</summary>
    public FakeConsole TypeAltGr(string text)
    {
        foreach (var c in text)
        {
            Keys.Enqueue(new ConsoleKeyInfo(c, default, shift: false, alt: true, control: true));
        }

        return this;
    }

    /// <summary>Ends a burst of keys: the keys after it arrive later, like the person typing again.</summary>
    public FakeConsole Pause()
    {
        Keys.Enqueue(null);
        return this;
    }

    public FakeConsole Press(ConsoleKey key, char keyChar = '\0', bool control = false)
    {
        Keys.Enqueue(Key(key, keyChar, control));
        return this;
    }

    private static ConsoleKeyInfo Letter(char c) => c switch
    {
        '\r' => Key(ConsoleKey.Enter, '\r'),
        '\b' => Key(ConsoleKey.Backspace, '\b'),
        _ => Key(char.IsAsciiLetter(c) ? (ConsoleKey)char.ToUpperInvariant(c) : default, c),
    };

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar, bool control = false) =>
        new(keyChar, key, shift: false, alt: false, control);

    private void Append(string text, bool toStandardOutput)
    {
        if (WriteException is not null && text == FailingWrite)
        {
            throw WriteException;
        }

        output.Append(text);
        if (toStandardOutput)
        {
            standardOutput.Append(text);
        }
    }
}
