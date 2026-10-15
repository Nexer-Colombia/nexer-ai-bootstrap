using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NexerAI.Core.Ports;

namespace NexerAI.Adapters.Credentials;

/// <summary>
/// Keeps each tracker's secret in <c>{folder}\{sha256(key)}.dpapi</c>, encrypted with Windows DPAPI for the
/// current user (design 5). The key is the tracker URL normalized to lowercase scheme, host and path, without
/// a default port or trailing slash, so one tracker written differently shares one entry. Neither the URL nor
/// the secret is stored in plain text, and neither appears in an exception message. DPAPI does not stop a
/// process running as the same user, which is why the baseline denies Claude reads of the folder.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStore : ICredentialStore
{
    /// <summary>
    /// Prefix of the DPAPI optional entropy. The entropy binds a file to its tracker key, so a file copied
    /// over another tracker's entry does not decrypt, and generic tools that call DPAPI without entropy cannot
    /// read the files. It is not a secret.
    /// </summary>
    private const string EntropyPrefix = "nexer-ai credential v1\n";

    private readonly string folder;

    /// <summary>Creates a store in <paramref name="folder"/>, usually <see cref="DefaultDirectory"/>; the folder is created on the first write.</summary>
    /// <param name="folder">Fully qualified path of the folder.</param>
    public DpapiCredentialStore(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"The credentials folder '{folder}' is not absolute.", nameof(folder));
        }

        this.folder = folder;
    }

    /// <summary>The folder of the current user: <c>%USERPROFILE%\.nexer-ai\credentials</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexer-ai", "credentials");

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="trackerUrl"/> is not an absolute http or https address without user info, query or fragment.</exception>
    public bool Exists(Uri trackerUrl) => File.Exists(PathOf(KeyOf(trackerUrl)));

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="trackerUrl"/> is not an absolute http or https address without user info, query or fragment.</exception>
    public string? Get(Uri trackerUrl)
    {
        var key = KeyOf(trackerUrl);
        byte[] blob;
        try
        {
            blob = File.ReadAllBytes(PathOf(key));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        var plain = ProtectedData.Unprotect(blob, EntropyOf(key), DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Writes a temporary file in the same folder, flushes it to disk and moves it over the entry, so a crash
    /// never leaves a half-written file. The last writer wins; there is no lock and no retry.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="trackerUrl"/> is not an absolute http or https address without user info, query or
    /// fragment, or <paramref name="secret"/> is empty or white space.
    /// </exception>
    public void Set(Uri trackerUrl, string secret)
    {
        var key = KeyOf(trackerUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var plain = Encoding.UTF8.GetBytes(secret);
        byte[] blob;
        try
        {
            blob = ProtectedData.Protect(plain, EntropyOf(key), DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }

        Directory.CreateDirectory(folder);
        var path = PathOf(key);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(blob);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Normalizes <paramref name="trackerUrl"/>: lowercase scheme, host and path (tracker organizations and
    /// collections are case-insensitive, as in remote normalization), no default port, no trailing slash.
    /// </summary>
    /// <remarks>The message never repeats the URL: user info, a query or a fragment may carry a secret.</remarks>
    private static string KeyOf(Uri trackerUrl)
    {
        ArgumentNullException.ThrowIfNull(trackerUrl);
        if (!trackerUrl.IsAbsoluteUri
            || (trackerUrl.Scheme != Uri.UriSchemeHttps && trackerUrl.Scheme != Uri.UriSchemeHttp)
            || trackerUrl.UserInfo.Length > 0
            || trackerUrl.Query.Length > 0
            || trackerUrl.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "A tracker URL must be an absolute http or https address without user info, query or fragment.", nameof(trackerUrl));
        }

        var serverAndScheme = trackerUrl.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped);
        return (serverAndScheme + trackerUrl.AbsolutePath.TrimEnd('/')).ToLowerInvariant();
    }

    private static byte[] EntropyOf(string key) => Encoding.UTF8.GetBytes(EntropyPrefix + key);

    private string PathOf(string key) =>
        Path.Combine(folder, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".dpapi");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: the original error matters more than a leftover temporary file.
        }
    }
}
