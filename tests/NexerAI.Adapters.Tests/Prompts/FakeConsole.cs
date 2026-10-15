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

    private bool treatControlCAsInput;

    public bool IsInputRedirected { get; init; }

    /// <summary>Thrown by <see cref="ReadKey"/> once the scripted keys run out, like a console that cannot read keys.</summary>
    public Exception? ReadKeyException { get; init; }

    /// <summary>Scripted keys; <see langword="null"/> is a pause, after which the next keys are not yet available.</summary>
    public Queue<ConsoleKeyInfo?> Keys { get; } = new();

    /// <summary>Lines for <see cref="ReadLine"/>; <see langword="null"/> is end of input.</summary>
    public Queue<string?> Lines { get; } = new();

    public bool TreatControlCAsInput
    {
        get => treatControlCAsInput;
        set
        {
            ControlCSettings.Add(value);
            treatControlCAsInput = value;
        }
    }

    /// <summary>Every value given to <see cref="TreatControlCAsInput"/>, in order.</summary>
    public List<bool> ControlCSettings { get; } = [];

    public string Output => output.ToString();

    public bool KeyAvailable => Keys.TryPeek(out var key) && key is not null;

    public ConsoleKeyInfo ReadKey()
    {
        while (Keys.TryDequeue(out var key))
        {
            if (key is { } pressed)
            {
                return pressed;
            }
        }

        throw ReadKeyException ?? new InvalidDataException("No scripted key left.");
    }

    public string? ReadLine() =>
        Lines.TryDequeue(out var line) ? line : throw new InvalidDataException("No scripted line left.");

    public void Write(string text) => output.Append(text);

    /// <summary>Queues one key per UTF-16 unit of <paramref name="text"/>, as typing or a paste arrives.</summary>
    public FakeConsole Type(string text)
    {
        foreach (var c in text)
        {
            Keys.Enqueue(c switch
            {
                '\r' => Key(ConsoleKey.Enter, '\r'),
                '\b' => Key(ConsoleKey.Backspace, '\b'),
                _ => Key(char.IsAsciiLetter(c) ? (ConsoleKey)char.ToUpperInvariant(c) : default, c),
            });
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

    private static ConsoleKeyInfo Key(ConsoleKey key, char keyChar, bool control = false) =>
        new(keyChar, key, shift: false, alt: false, control);
}
