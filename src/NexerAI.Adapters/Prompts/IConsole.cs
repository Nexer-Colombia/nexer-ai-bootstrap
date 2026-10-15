namespace NexerAI.Adapters.Prompts;

/// <summary>The few console calls <see cref="ConsolePrompter"/> makes, so tests can script a person at a keyboard.</summary>
internal interface IConsole
{
    /// <summary>Whether standard input comes from a pipe or a file instead of the keyboard.</summary>
    bool IsInputRedirected { get; }

    /// <summary>Whether standard output goes to a pipe or a file.</summary>
    bool IsOutputRedirected { get; }

    /// <summary>Whether standard error goes to a pipe or a file.</summary>
    bool IsErrorRedirected { get; }

    /// <summary>Whether Ctrl+C arrives as a key instead of ending the process.</summary>
    /// <exception cref="IOException">There is no console.</exception>
    bool TreatControlCAsInput { get; set; }

    /// <summary>Whether a key is waiting, such as the rest of a paste.</summary>
    bool KeyAvailable { get; }

    /// <summary>Reads one key without echoing it.</summary>
    /// <exception cref="InvalidOperationException">The console cannot read keys.</exception>
    ConsoleKeyInfo ReadKey();

    /// <summary>Reads one line with the console's own echo and editing; <see langword="null"/> at end of input.</summary>
    string? ReadLine();

    /// <summary>Writes <paramref name="text"/> to standard error, where questions go by default.</summary>
    void Write(string text);

    /// <summary>Writes <paramref name="text"/> to standard output, used when standard error is redirected.</summary>
    void WriteOutput(string text);
}

/// <summary>
/// The real console. Questions go to standard error, like git and gh prompts, so standard output keeps only a
/// command's results; <see cref="ConsolePrompter"/> switches to standard output when only standard error is redirected.
/// </summary>
internal sealed class SystemConsole : IConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool TreatControlCAsInput
    {
        get => Console.TreatControlCAsInput;
        set => Console.TreatControlCAsInput = value;
    }

    public bool KeyAvailable => Console.KeyAvailable;

    public ConsoleKeyInfo ReadKey() => Console.ReadKey(intercept: true);

    public string? ReadLine() => Console.ReadLine();

    public void Write(string text) => Console.Error.Write(text);

    public void WriteOutput(string text) => Console.Out.Write(text);
}
