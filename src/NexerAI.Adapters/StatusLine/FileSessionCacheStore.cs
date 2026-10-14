using System.Text.Json;
using System.Text.Json.Serialization;
using NexerAI.Core.Ports;
using NexerAI.Core.StatusLine;

namespace NexerAI.Adapters.StatusLine;

/// <summary>
/// Keeps each session's <see cref="CacheSession"/> in <c>{folder}\{session_id}.json</c> (design 4.5). An id
/// not accepted by <see cref="SessionId.IsFileName"/> is never joined to the folder. Writes go to a temporary
/// file in the same folder that is then moved over the session file. A status line must always print, so
/// every file error is ignored: a failed read is no memory, a failed write or delete changes nothing.
/// </summary>
public sealed class FileSessionCacheStore : ISessionCacheStore
{
    private static readonly EnumerationOptions TopLevelJsonFiles = new()
    {
        MatchType = MatchType.Simple,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = true,
    };

    private readonly string folder;

    /// <summary>Creates a store in <paramref name="folder"/>, usually <see cref="DefaultDirectory"/>; the folder is created on the first save.</summary>
    /// <param name="folder">Fully qualified path of the folder.</param>
    public FileSessionCacheStore(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"The status line folder '{folder}' is not absolute.", nameof(folder));
        }

        this.folder = folder;
    }

    /// <summary>The folder of the current user: <c>%USERPROFILE%\.nexer-ai\statusline</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexer-ai", "statusline");

    /// <inheritdoc />
    public CacheSession? Load(string sessionId)
    {
        if (!SessionId.IsFileName(sessionId))
        {
            return null;
        }

        try
        {
            // Share everything, so a reader never makes another run's move or delete fail.
            using var stream = new FileStream(PathOf(sessionId), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(stream, SessionCacheJsonContext.Default.SessionDocument)?.ToSession();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Save(string sessionId, CacheSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!SessionId.IsFileName(sessionId))
        {
            return;
        }

        var tempPath = Path.Combine(folder, $"{sessionId}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(tempPath, JsonSerializer.SerializeToUtf8Bytes(SessionDocument.From(session), SessionCacheJsonContext.Default.SessionDocument));
            File.Move(tempPath, PathOf(sessionId), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(tempPath);
        }
    }

    /// <inheritdoc />
    public void Delete(string sessionId)
    {
        if (SessionId.IsFileName(sessionId))
        {
            TryDelete(PathOf(sessionId));
        }
    }

    /// <inheritdoc />
    /// <remarks>Only <c>*.json</c> files directly in the folder; links and subfolders are never followed.</remarks>
    public void SweepOlderThan(DateTimeOffset cutoff)
    {
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.json", TopLevelJsonFiles))
            {
                if (file.LastWriteTimeUtc < cutoff.UtcDateTime)
                {
                    TryDelete(file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable folder has nothing to sweep.
        }
    }

    private string PathOf(string sessionId) => Path.Combine(folder, sessionId + ".json");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or denied: the sweep tries again on a later run.
        }
    }

    internal sealed class SessionDocument
    {
        public long? Requests { get; set; }

        public long? LastResponseAt { get; set; }

        public bool? RenewalSeen { get; set; }

        public static SessionDocument From(CacheSession session) => new()
        {
            Requests = session.Requests,
            LastResponseAt = session.LastResponseAt?.ToUnixTimeSeconds(),
            RenewalSeen = session.RenewalSeen,
        };

        /// <exception cref="ArgumentOutOfRangeException">The last response time is out of range.</exception>
        public CacheSession? ToSession() =>
            Requests is { } requests && RenewalSeen is { } renewalSeen
                ? new CacheSession(
                    requests,
                    LastResponseAt is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
                    renewalSeen)
                : null;
    }
}

/// <summary>Source-generated serializer for <see cref="FileSessionCacheStore"/>, so Native AOT needs no reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(FileSessionCacheStore.SessionDocument))]
internal sealed partial class SessionCacheJsonContext : JsonSerializerContext;
