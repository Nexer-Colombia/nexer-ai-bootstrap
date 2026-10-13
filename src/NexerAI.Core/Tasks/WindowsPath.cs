namespace NexerAI.Core.Tasks;

/// <summary>
/// An absolute Windows path, drive (<c>C:/</c>) or UNC (<c>//server/share</c>), with forward slashes,
/// no empty or <c>.</c> folders and <c>..</c> resolved. Case is kept. Pure string logic, no I/O.
/// </summary>
internal sealed class WindowsPath
{
    private readonly string root;

    private readonly string[] folders;

    private WindowsPath(string root, string[] folders)
    {
        this.root = root;
        this.folders = folders;
    }

    /// <summary>The parent folder, or <see langword="null"/> at the root.</summary>
    public WindowsPath? Parent => folders.Length == 0 ? null : new(root, folders[..^1]);

    /// <summary>Parses an absolute path with either separator; returns <see langword="null"/> for anything else.</summary>
    public static WindowsPath? Parse(string path)
    {
        var text = path.Replace('\\', '/');
        string root;
        string rest;
        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            var parts = text[2..].Split('/', 3);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return null;
            }

            root = $"//{parts[0]}/{parts[1]}";
            rest = parts.Length == 3 ? parts[2] : string.Empty;
        }
        else if (text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '/')
        {
            root = text[..3];
            rest = text[3..];
        }
        else
        {
            return null; // Relative, drive-relative (C:folder) or not a Windows path.
        }

        var folders = new List<string>();
        foreach (var folder in rest.Split('/'))
        {
            if (folder == "..")
            {
                if (folders.Count > 0)
                {
                    folders.RemoveAt(folders.Count - 1);
                }
            }
            else if (folder is not ("" or "."))
            {
                folders.Add(folder);
            }
        }

        return new WindowsPath(root, [.. folders]);
    }

    public WindowsPath Combine(string folder) => new(root, [.. folders, folder]);

    public override string ToString() =>
        folders.Length == 0 ? root : $"{root.TrimEnd('/')}/{string.Join('/', folders)}";
}
