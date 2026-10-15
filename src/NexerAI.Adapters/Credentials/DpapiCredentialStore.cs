using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NexerAI.Core.Credentials;
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

    private const int ErrorSharingViolation = unchecked((int)0x80070020);

    private const int ErrorLockViolation = unchecked((int)0x80070021);

    /// <summary>How long <see cref="Get"/> retries a file another program holds open, such as an antivirus scan or a concurrent <see cref="Set"/>.</summary>
    private static readonly TimeSpan SharingTimeout = TimeSpan.FromMilliseconds(500);

    private readonly string folder;

    private readonly Action<TimeSpan> wait;

    /// <summary>Creates a store in <paramref name="folder"/>, usually <see cref="DefaultDirectory"/>; the folder is created on the first write.</summary>
    /// <param name="folder">Fully qualified path of the folder.</param>
    public DpapiCredentialStore(string folder)
        : this(folder, Thread.Sleep)
    {
    }

    /// <summary>Creates a store that calls <paramref name="wait"/> between read attempts, so tests control the waits.</summary>
    internal DpapiCredentialStore(string folder, Action<TimeSpan> wait)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!Path.IsPathFullyQualified(folder))
        {
            throw new ArgumentException($"The credentials folder '{folder}' is not absolute.", nameof(folder));
        }

        this.folder = folder;
        this.wait = wait;
    }

    /// <summary>The folder of the current user: <c>%USERPROFILE%\.nexer-ai\credentials</c>.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nexer-ai", "credentials");

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="trackerUrl"/> is not an absolute http or https address without user info, query or fragment.</exception>
    public bool Exists(Uri trackerUrl) => File.Exists(PathOf(KeyOf(trackerUrl)));

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="trackerUrl"/> is not an absolute http or https address without user info, query or fragment.</exception>
    /// <exception cref="CredentialStoreException">
    /// The file cannot be read (after retrying for <see cref="SharingTimeout"/> while another program holds it) or
    /// does not decrypt; the message names its path, never the tracker, and the file is left untouched.
    /// </exception>
    public string? Get(Uri trackerUrl)
    {
        var key = KeyOf(trackerUrl);
        byte[]? blob;
        try
        {
            blob = Read(PathOf(key));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CredentialStoreException(
                $"Cannot read the credential file '{PathOf(key)}': another program keeps it open or the current Windows user cannot read it.",
                ex);
        }

        if (blob is null)
        {
            return null;
        }

        byte[] plain;
        try
        {
            plain = ProtectedData.Unprotect(blob, EntropyOf(key), DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new CredentialStoreException(
                $"Cannot decrypt the credential file '{PathOf(key)}': it is corrupt, was copied from another entry or belongs to another Windows user or machine. Run 'nexer-ai project' to enter the credential again.",
                ex);
        }

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

    /// <summary>
    /// Reads the file, or returns <see langword="null"/> when it or its folder is missing. A sharing or lock
    /// violation is retried with a growing delay, the last one shortened so the waits add up to exactly
    /// <see cref="SharingTimeout"/> and the last attempt comes at its end, like <c>FileStateStore</c>. The waits
    /// are counted rather than the clock, so a busy machine that oversleeps (Windows rounds a 10 ms sleep up to
    /// about 15.6 ms) still makes every attempt.
    /// </summary>
    private byte[]? Read(string path)
    {
        var waited = TimeSpan.Zero;
        var delay = TimeSpan.FromMilliseconds(10);
        while (true)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
            catch (IOException ex) when (ex.HResult is ErrorSharingViolation or ErrorLockViolation && waited < SharingTimeout)
            {
                var remaining = SharingTimeout - waited;
                var next = delay < remaining ? delay : remaining;
                wait(next);
                waited += next;
                delay *= 2;
            }
        }
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
