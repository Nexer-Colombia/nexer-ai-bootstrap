using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NexerAI.Adapters.Credentials;
using NexerAI.Core.Credentials;

namespace NexerAI.Adapters.Tests.Credentials;

[SupportedOSPlatform("windows")]
public sealed class DpapiCredentialStoreTests : IDisposable
{
    private const string Secret = "pat-Ünïcødé-7f3a9c1e5b";

    private static readonly Uri Acme = new("https://dev.azure.com/acme");

    private readonly TempDirectory root = new();

    private readonly string folder;

    private readonly DpapiCredentialStore store;

    public DpapiCredentialStoreTests()
    {
        folder = Path.Combine(root.Path, ".nexer-ai", "credentials");
        store = new DpapiCredentialStore(folder);
    }

    public void Dispose() => root.Dispose();

    [Fact]
    public void A_missing_folder_or_entry_is_no_secret()
    {
        Assert.False(store.Exists(Acme));
        Assert.Null(store.Get(Acme));

        Directory.CreateDirectory(folder);

        Assert.False(store.Exists(Acme));
        Assert.Null(store.Get(Acme));
    }

    [Fact]
    public void Set_creates_the_folder_and_another_instance_reads_the_secret_back()
    {
        store.Set(Acme, Secret);

        var other = new DpapiCredentialStore(folder);
        Assert.True(other.Exists(Acme));
        Assert.Equal(Secret, other.Get(Acme));
    }

    [Fact]
    public void Set_replaces_the_secret_and_leaves_one_file_and_no_temporary_files()
    {
        store.Set(Acme, "old-token");
        store.Set(Acme, Secret);

        Assert.Equal(Secret, store.Get(Acme));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Theory]
    [InlineData("https://DEV.Azure.com/Acme/")]
    [InlineData("HTTPS://dev.azure.com:443/acme")]
    [InlineData("https://dev.azure.com/ACME///")]
    public void The_same_tracker_written_differently_shares_one_entry(string written)
    {
        store.Set(new Uri(written), Secret);

        Assert.Equal(Secret, store.Get(Acme));
        Assert.Single(Directory.GetFiles(folder));
    }

    [Theory]
    [InlineData("https://dev.azure.com/acme-qa")]
    [InlineData("http://dev.azure.com/acme")]
    [InlineData("https://dev.azure.com:8443/acme")]
    [InlineData("https://acme.atlassian.net")]
    public void Different_trackers_have_separate_entries(string other)
    {
        store.Set(Acme, Secret);

        Assert.False(store.Exists(new Uri(other)));
        Assert.Null(store.Get(new Uri(other)));
    }

    [Fact]
    public void The_stored_file_holds_neither_the_secret_nor_the_tracker_url()
    {
        store.Set(Acme, Secret);

        var file = Assert.Single(Directory.GetFiles(folder));
        Assert.Matches("^[0-9a-f]{64}\\.dpapi$", Path.GetFileName(file));
        var bytes = File.ReadAllBytes(file);
        foreach (var plain in new[] { Secret, "dev.azure.com", "acme" })
        {
            Assert.False(Contains(bytes, Encoding.UTF8.GetBytes(plain)), $"UTF-8 '{plain}' found in the file.");
            Assert.False(Contains(bytes, Encoding.Unicode.GetBytes(plain)), $"UTF-16 '{plain}' found in the file.");
        }
    }

    [Fact]
    public void A_corrupt_file_is_reported_with_its_path_and_left_untouched()
    {
        store.Set(Acme, Secret);
        var file = Assert.Single(Directory.GetFiles(folder));
        File.WriteAllBytes(file, [1, 2, 3]);

        var ex = Assert.Throws<CredentialStoreException>(() => store.Get(Acme));

        Assert.Contains(file, ex.Message, StringComparison.Ordinal);
        Assert.Contains("nexer-ai project", ex.Message, StringComparison.Ordinal);
        Assert.IsType<CryptographicException>(ex.InnerException);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
    }

    [Fact]
    public void A_file_copied_over_another_trackers_entry_does_not_decrypt_and_the_secret_is_not_in_the_message()
    {
        var other = new Uri("https://acme.atlassian.net");
        store.Set(Acme, Secret);
        var acmeFile = Assert.Single(Directory.GetFiles(folder));
        store.Set(other, "other-token");
        var otherFile = Directory.GetFiles(folder).Single(f => f != acmeFile);
        File.Copy(acmeFile, otherFile, overwrite: true);

        var ex = Assert.Throws<CredentialStoreException>(() => store.Get(other));

        Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, ex.ToString(), StringComparison.Ordinal);
        Assert.Equal(Secret, store.Get(Acme));
    }

    [Theory]
    [InlineData("dev.azure.com/acme", UriKind.Relative)]
    [InlineData("file:///C:/tokens", UriKind.Absolute)]
    [InlineData("https://me:hunter2@dev.azure.com/acme", UriKind.Absolute)]
    [InlineData("https://dev.azure.com/acme?token=hunter2", UriKind.Absolute)]
    [InlineData("https://dev.azure.com/acme#hunter2", UriKind.Absolute)]
    public void A_url_that_is_not_a_plain_tracker_address_is_rejected_without_echoing_it(string url, UriKind kind)
    {
        var uri = new Uri(url, kind);

        var exceptions = new[]
        {
            Assert.Throws<ArgumentException>(() => store.Exists(uri)),
            Assert.Throws<ArgumentException>(() => store.Get(uri)),
            Assert.Throws<ArgumentException>(() => store.Set(uri, Secret)),
        };

        Assert.All(exceptions, ex => Assert.DoesNotContain("hunter2", ex.Message, StringComparison.Ordinal));
        Assert.All(exceptions, ex => Assert.DoesNotContain(Secret, ex.Message, StringComparison.Ordinal));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Set_rejects_an_empty_secret()
    {
        Assert.Throws<ArgumentException>(() => store.Set(Acme, ""));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Constructor_rejects_a_relative_folder()
    {
        Assert.Throws<ArgumentException>(() => new DpapiCredentialStore(@".nexer-ai\credentials"));
    }

    [Fact]
    public void DefaultDirectory_is_the_credentials_folder_in_the_user_profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, ".nexer-ai", "credentials"), DpapiCredentialStore.DefaultDirectory);
    }

    private static bool Contains(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle) >= 0;
}
