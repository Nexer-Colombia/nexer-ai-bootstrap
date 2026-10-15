namespace NexerAI.Adapters.Prompts;

/// <summary>The few console calls <see cref="ConsolePrompter"/> makes, so tests can script a person at a keyboard.</summary>
internal interface IConsole
{
    /// <summary>Whether standard input comes from a pipe or a file instead of the keyboard.</summary>
    bool IsInputRedirected { get; }

    /// <summary>Reads one line with the console's own echo and editing; <see langword="null"/> at end of input.</summary>
    string? ReadLine();

    /// <summary>Writes <paramref name="text"/> where the person reads the questions.</summary>
    void Write(string text);
}

/// <summary>
/// The real console. Questions go to standard error, like git and gh prompts, so standard output keeps only a
/// command's results.
/// </summary>
internal sealed class SystemConsole : IConsole
{
    public bool IsInputRedirected => Console.IsInputRedirected;

    public string? ReadLine() => Console.ReadLine();

    public void Write(string text) => Console.Error.Write(text);
}
