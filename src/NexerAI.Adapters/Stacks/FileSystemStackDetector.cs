using System.Xml;
using System.Xml.Linq;
using NexerAI.Core.Domain;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Stacks;

/// <summary>
/// Detects stack signals by walking the files of a working copy (design 4.4). Build output,
/// dependency and git folders are skipped, links are not followed, and entries that cannot be
/// read are ignored.
/// </summary>
public sealed class FileSystemStackDetector : IStackDetector
{
    /// <summary>
    /// Signal rules, in the order their signals are reported. A new signal is one entry here, added
    /// together with the plugin it suggests.
    /// </summary>
    private static readonly StackRule[] Rules =
    [
        new(new StackSignal("Umbraco", "nexer-dev-umbraco"), ReferencesUmbracoCms),
        new(new StackSignal("Playwright", null), file => HasConfigName(file, "playwright.config.")),
        new(new StackSignal("Cypress", null), file => HasConfigName(file, "cypress.config.")),
    ];

    private static readonly HashSet<string> SkippedDirectories =
        new([".git", "bin", "obj", "node_modules"], StringComparer.OrdinalIgnoreCase);

    private static readonly EnumerationOptions Enumeration = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        MatchType = MatchType.Simple,
    };

    private static readonly XmlReaderSettings XmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <inheritdoc />
    /// <exception cref="DirectoryNotFoundException"><paramref name="projectPath"/> is not an existing directory.</exception>
    public IReadOnlyList<StackSignal> Detect(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Project directory '{projectPath}' does not exist.");
        }

        var found = new bool[Rules.Length];
        var remaining = Rules.Length;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(projectPath));

        while (pending.Count > 0 && remaining > 0)
        {
            foreach (var entry in Entries(pending.Pop()))
            {
                if (entry is DirectoryInfo directory)
                {
                    if (!directory.Attributes.HasFlag(FileAttributes.ReparsePoint) && !SkippedDirectories.Contains(directory.Name))
                    {
                        pending.Push(directory);
                    }
                }
                else if (entry is FileInfo file)
                {
                    for (var i = 0; i < Rules.Length; i++)
                    {
                        if (!found[i] && Rules[i].Matches(file))
                        {
                            found[i] = true;
                            remaining--;
                        }
                    }
                }
            }
        }

        return [.. Rules.Where((_, i) => found[i]).Select(rule => rule.Signal)];
    }

    private static List<FileSystemInfo> Entries(DirectoryInfo directory)
    {
        try
        {
            return [.. directory.EnumerateFileSystemInfos("*", Enumeration)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool HasConfigName(FileInfo file, string prefix) =>
        file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && file.Name.Length > prefix.Length;

    /// <summary>
    /// True for a <c>.csproj</c> with a <c>PackageReference</c> to <c>UmbracoCms</c> (Umbraco 8) or to
    /// <c>Umbraco.Cms</c> or one of its <c>Umbraco.Cms.*</c> packages (Umbraco 9 and later). Element
    /// names are matched without their XML namespace, so old-style MSBuild projects work too; a file
    /// that cannot be read or parsed counts as no match.
    /// </summary>
    private static bool ReferencesUmbracoCms(FileInfo file)
    {
        if (!file.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        XDocument project;
        try
        {
            using var reader = XmlReader.Create(file.FullName, XmlSettings);
            project = XDocument.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return project
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => element.Attribute("Include")?.Value.Trim())
            .Any(IsUmbracoCmsPackage);
    }

    private static bool IsUmbracoCmsPackage(string? package) =>
        package is not null
        && (package.Equals("UmbracoCms", StringComparison.OrdinalIgnoreCase)
            || package.Equals("Umbraco.Cms", StringComparison.OrdinalIgnoreCase)
            || package.StartsWith("Umbraco.Cms.", StringComparison.OrdinalIgnoreCase));

    private sealed record StackRule(StackSignal Signal, Func<FileInfo, bool> Matches);
}
