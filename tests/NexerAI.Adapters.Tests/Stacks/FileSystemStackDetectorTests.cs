using System.Diagnostics;
using NexerAI.Adapters.Stacks;
using NexerAI.Core.Domain;

namespace NexerAI.Adapters.Tests.Stacks;

public sealed class FileSystemStackDetectorTests : IDisposable
{
    private static readonly StackSignal Umbraco = new("Umbraco", "nexer-dev-umbraco");

    private static readonly StackSignal Playwright = new("Playwright", null);

    private static readonly StackSignal Cypress = new("Cypress", null);

    private readonly TempDirectory root = new();

    private readonly FileSystemStackDetector detector = new();

    public void Dispose() => root.Dispose();

    [Theory]
    [InlineData("UmbracoCms")]
    [InlineData("Umbraco.Cms")]
    [InlineData("Umbraco.Cms.Web.Website")]
    [InlineData("umbraco.cms.core")]
    [InlineData("UMBRACOCMS")]
    public void Detect_finds_umbraco_from_a_package_reference(string package)
    {
        root.WriteFile(@"src\Site\Site.csproj", SdkProject($"""<PackageReference Include="{package}" Version="13.0.0" />"""));

        Assert.Equal([Umbraco], detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_finds_umbraco_in_an_msbuild_namespaced_project()
    {
        root.WriteFile("Site.csproj", """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <PackageReference Include="UmbracoCms">
                  <Version>8.18.0</Version>
                </PackageReference>
              </ItemGroup>
            </Project>
            """);

        Assert.Equal([Umbraco], detector.Detect(root.Path));
    }

    [Theory]
    [InlineData("Newtonsoft.Json")]
    [InlineData("Umbraco.Forms")]
    [InlineData("Umbraco.CmsExtensions")]
    [InlineData("Our.Umbraco.Cms")]
    public void Detect_ignores_projects_without_an_umbraco_cms_package(string package)
    {
        root.WriteFile("Site.csproj", SdkProject($"""<PackageReference Include="{package}" Version="1.0.0" />"""));

        Assert.Empty(detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_ignores_umbraco_outside_a_csproj()
    {
        root.WriteFile("Site.fsproj", SdkProject("""<PackageReference Include="Umbraco.Cms" Version="13.0.0" />"""));
        root.WriteFile("Directory.Packages.props", SdkProject("""<PackageVersion Include="Umbraco.Cms" Version="13.0.0" />"""));

        Assert.Empty(detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_skips_a_malformed_csproj_and_keeps_looking()
    {
        root.WriteFile(@"a\Broken.csproj", "<Project><ItemGroup>");
        root.WriteFile(@"b\Site.csproj", SdkProject("""<PackageReference Include="Umbraco.Cms" Version="13.0.0" />"""));

        Assert.Equal([Umbraco], detector.Detect(root.Path));
    }

    [Theory]
    [InlineData("playwright.config.ts")]
    [InlineData("playwright.config.js")]
    [InlineData("Playwright.Config.MJS")]
    public void Detect_finds_playwright_from_its_config_file(string fileName)
    {
        root.WriteFile(fileName);

        Assert.Equal([Playwright], detector.Detect(root.Path));
    }

    [Theory]
    [InlineData("cypress.config.ts")]
    [InlineData("CYPRESS.CONFIG.JS")]
    public void Detect_finds_cypress_from_its_config_file(string fileName)
    {
        root.WriteFile(fileName);

        Assert.Equal([Cypress], detector.Detect(root.Path));
    }

    [Theory]
    [InlineData("playwright.config")]
    [InlineData("my-playwright.config.ts")]
    [InlineData("cypress.json")]
    public void Detect_ignores_files_that_only_resemble_a_config_file(string fileName)
    {
        root.WriteFile(fileName);

        Assert.Empty(detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_finds_signals_in_nested_folders_in_a_fixed_order()
    {
        root.WriteFile(@"tests\e2e\cypress\cypress.config.ts");
        root.WriteFile(@"tests\e2e\playwright\playwright.config.ts");
        root.WriteFile(@"src\Web\Site\Site.csproj", SdkProject("""<PackageReference Include="Umbraco.Cms" Version="13.0.0" />"""));

        Assert.Equal([Umbraco, Playwright, Cypress], detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_reports_each_signal_once()
    {
        root.WriteFile(@"a\playwright.config.ts");
        root.WriteFile(@"b\playwright.config.js");
        root.WriteFile(@"a\A.csproj", SdkProject("""<PackageReference Include="UmbracoCms" Version="8.18.0" />"""));
        root.WriteFile(@"b\B.csproj", SdkProject("""<PackageReference Include="Umbraco.Cms" Version="13.0.0" />"""));

        Assert.Equal([Umbraco, Playwright], detector.Detect(root.Path));
    }

    [Theory]
    [InlineData(".git")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData("Node_Modules")]
    [InlineData("BIN")]
    public void Detect_skips_build_output_dependency_and_git_folders(string folder)
    {
        root.WriteFile($@"{folder}\playwright.config.ts");
        root.WriteFile($@"src\{folder}\cypress.config.ts");
        root.WriteFile($@"{folder}\Site\Site.csproj", SdkProject("""<PackageReference Include="Umbraco.Cms" Version="13.0.0" />"""));

        Assert.Empty(detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_scans_a_packages_folder_because_javascript_monorepos_keep_their_sources_there()
    {
        root.WriteFile(@"packages\web\playwright.config.ts");

        Assert.Equal([Playwright], detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_does_not_follow_directory_junctions()
    {
        using var outside = new TempDirectory();
        outside.WriteFile("playwright.config.ts");
        var link = Path.Combine(root.Path, "linked");
        CreateJunction(link, outside.Path);
        try
        {
            Assert.True(File.Exists(Path.Combine(link, "playwright.config.ts")));
            Assert.Empty(detector.Detect(root.Path));
        }
        finally
        {
            // Removes the junction only; the target stays for its own cleanup.
            Directory.Delete(link);
        }
    }

    [Fact]
    public void Detect_returns_nothing_for_an_empty_directory()
    {
        Assert.Empty(detector.Detect(root.Path));
    }

    [Fact]
    public void Detect_rejects_a_missing_directory()
    {
        var missing = Path.Combine(root.Path, "missing");

        Assert.Throws<DirectoryNotFoundException>(() => detector.Detect(missing));
    }

    [Fact]
    public void Detect_rejects_a_file_path()
    {
        var file = root.WriteFile("playwright.config.ts");

        Assert.Throws<DirectoryNotFoundException>(() => detector.Detect(file));
    }

    [Fact]
    public void Detect_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => detector.Detect(null!));
    }

    private static void CreateJunction(string link, string target)
    {
        // Junctions, unlike symbolic links, need no privilege on Windows.
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        mklink.StandardOutput.ReadToEnd();
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    private static string SdkProject(string item) => $"""
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <ItemGroup>
            {item}
          </ItemGroup>
        </Project>
        """;
}
