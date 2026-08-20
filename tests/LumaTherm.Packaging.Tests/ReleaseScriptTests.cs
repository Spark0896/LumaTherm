using System.Security.Cryptography;
using System.Text.Json;

namespace LumaTherm.Packaging.Tests;

public sealed class ReleaseScriptTests
{
    [Fact]
    public void PlanRejectsPublisherThatDoesNotMatchCertificateBeforeToolExecution()
    {
        using var fixture = ReleaseFixture.Create();
        var result = fixture.RunBuild("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkPath, "-Publisher", "CN=Wrong", "-CertificatePath", fixture.CertificatePath, "-CertificatePassword", fixture.CertificatePassword);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Publisher", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(fixture.RepositoryRoot, "*.msix", SearchOption.AllDirectories));
    }

    [Fact]
    public void PlanUsesAbsoluteX64ToolsAndExactPublishSwitchesWithoutDisclosingPassword()
    {
        using var fixture = ReleaseFixture.Create();
        var result = fixture.RunBuild("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkPath, "-CertificatePath", fixture.CertificatePath, "-CertificatePassword", fixture.CertificatePassword);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        Assert.DoesNotContain(fixture.CertificatePassword, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        var makeAppx = root.GetProperty("makeAppxPath").GetString()!;
        var signTool = root.GetProperty("signToolPath").GetString()!;
        Assert.True(Path.IsPathFullyQualified(makeAppx));
        Assert.True(Path.IsPathFullyQualified(signTool));
        Assert.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", makeAppx, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"{Path.DirectorySeparatorChar}x64{Path.DirectorySeparatorChar}", signTool, StringComparison.OrdinalIgnoreCase);
        var publish = root.GetProperty("publishArguments").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("win-x64", publish);
        Assert.Contains("--self-contained", publish);
        Assert.Contains("true", publish);
        Assert.Contains("-p:PublishSingleFile=true", publish);
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", publish);
        Assert.Contains("-p:DebugType=None", publish);
        Assert.Contains("-p:DebugSymbols=false", publish);
        Assert.Contains(publish, value => value!.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));
        Assert.Contains("-p:UseArtifactsOutput=true", publish);
        var restore = root.GetProperty("restoreArguments").EnumerateArray().Select(e => e.GetString()).ToArray();
        var test = root.GetProperty("testArguments").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains(restore, value => value!.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));
        Assert.Contains("-p:UseArtifactsOutput=true", restore);
        Assert.Contains("win-x64", restore);
        Assert.Contains(test, value => value!.StartsWith("-p:ArtifactsPath=", StringComparison.Ordinal));
        Assert.Contains("-p:UseArtifactsOutput=true", test);
        Assert.Equal("LumaTherm-1.0.0-win-x64.msix", root.GetProperty("msixName").GetString());
        Assert.Equal("LumaTherm-1.0.0-portable-win-x64.zip", root.GetProperty("zipName").GetString());
    }

    [Fact]
    public void PlanRejectsInvalidCertificatePasswordWithoutDisclosingIt()
    {
        using var fixture = ReleaseFixture.Create();
        const string wrongPassword = "wrong-secret-value";
        var result = fixture.RunBuild("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkPath, "-CertificatePath", fixture.CertificatePath, "-CertificatePassword", wrongPassword);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain(wrongPassword, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void PrepareLayoutDeletesStaleFilesAndCopiesOnlyPackageInputs()
    {
        using var fixture = ReleaseFixture.Create();
        var publish = Path.Combine(fixture.RepositoryRoot, "artifacts", "publish");
        Directory.CreateDirectory(publish);
        File.WriteAllText(Path.Combine(publish, "LumaTherm.App.exe"), "app");
        var layout = Path.Combine(fixture.RepositoryRoot, "artifacts", "package-layout");
        Directory.CreateDirectory(layout);
        File.WriteAllText(Path.Combine(layout, "stale.bin"), "stale");

        var result = fixture.RunBuild("-Mode", "PrepareLayout", "-PublishedAppPath", publish);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        Assert.False(File.Exists(Path.Combine(layout, "stale.bin")));
        Assert.True(File.Exists(Path.Combine(layout, "LumaTherm.exe")));
        Assert.False(File.Exists(Path.Combine(layout, "LumaTherm.App.exe")));
        Assert.True(File.Exists(Path.Combine(layout, "AppxManifest.xml")));
        Assert.True(File.Exists(Path.Combine(layout, "Assets", "StoreLogo.png")));
        Assert.True(Directory.Exists(Path.Combine(layout, "public")));
        Assert.All(Directory.GetFiles(fixture.RepositoryRoot, "*", SearchOption.AllDirectories), path => Assert.StartsWith(fixture.RepositoryRoot, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ChecksumsCoverEveryDistributedArtifactWithStableRelativeNames()
    {
        using var fixture = ReleaseFixture.Create();
        var dist = Path.Combine(fixture.RepositoryRoot, "dist");
        Directory.CreateDirectory(dist);
        foreach (var name in new[] { "LumaTherm-1.0.0-win-x64.msix", "LumaTherm-1.0.0-portable-win-x64.zip", "LumaTherm.cer", "install.ps1", "uninstall.ps1" })
        {
            File.WriteAllText(Path.Combine(dist, name), name);
        }
        var unexpected = Path.Combine(dist, "unexpected.exe");
        File.WriteAllText(unexpected, "stale");
        var rejected = fixture.RunBuild("-Mode", "EmitChecksums");
        Assert.NotEqual(0, rejected.ExitCode);
        File.Delete(unexpected);

        var result = fixture.RunBuild("-Mode", "EmitChecksums");

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        var lines = File.ReadAllLines(Path.Combine(dist, "SHA256SUMS.txt"));
        Assert.Equal(5, lines.Length);
        var names = lines.Select(line => line[(line.IndexOf(" *", StringComparison.Ordinal) + 2)..]).ToArray();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        Assert.Equal(5, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(lines, line => Assert.Matches("^[0-9A-F]{64} \\*[^\\/]+$", line));
    }

    [Fact]
    public void AssetBuilderRegeneratesApprovedPngsByteForByte()
    {
        var fixtureRoot = Path.Combine(RepositoryLayout.Root, "artifacts", "asset-parity", Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(fixtureRoot, "src", "LumaTherm.App", "Assets");
            Directory.CreateDirectory(source);
            File.Copy(Path.Combine(RepositoryLayout.Root, "src", "LumaTherm.App", "Assets", "LogoGeometry.xaml"), Path.Combine(source, "LogoGeometry.xaml"));
            var builder = Path.Combine(RepositoryLayout.Root, "tools", "LumaTherm.AssetBuilder", "bin", "Release", "net8.0-windows10.0.22621.0", "LumaTherm.AssetBuilder.exe");
            if (!File.Exists(builder))
            {
                builder = Path.Combine(RepositoryLayout.Root, "tools", "LumaTherm.AssetBuilder", "bin", "Debug", "net8.0-windows10.0.22621.0", "LumaTherm.AssetBuilder.exe");
            }

            var start = new System.Diagnostics.ProcessStartInfo(builder, $"\"{fixtureRoot}\"") { UseShellExecute = false, CreateNoWindow = true };
            using var process = System.Diagnostics.Process.Start(start)!;
            Assert.True(process.WaitForExit(10_000));
            Assert.Equal(0, process.ExitCode);
            foreach (var name in new[] { "StoreLogo.png", "Square44x44Logo.png", "Square150x150Logo.png", "Wide310x150Logo.png" })
            {
                Assert.Equal(
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(RepositoryLayout.Root, "packaging", "Assets", name))),
                    SHA256.HashData(File.ReadAllBytes(Path.Combine(fixtureRoot, "packaging", "Assets", name))));
            }
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private ReleaseFixture(string root, string repositoryRoot, string sdkPath, string certificatePath, string certificatePassword)
        {
            Root = root;
            RepositoryRoot = repositoryRoot;
            SdkPath = sdkPath;
            CertificatePath = certificatePath;
            CertificatePassword = certificatePassword;
        }

        public string Root { get; }
        public string RepositoryRoot { get; }
        public string SdkPath { get; }
        public string CertificatePath { get; }
        public string CertificatePassword { get; }

        public static ReleaseFixture Create()
        {
            var root = Path.Combine(RepositoryLayout.Root, "artifacts", "packaging-tests", Guid.NewGuid().ToString("N"));
            var repository = Path.Combine(root, "repository");
            Directory.CreateDirectory(Path.Combine(repository, "scripts"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "Assets"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "public"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"), Path.Combine(repository, "scripts", "build-release.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "packaging", "AppxManifest.xml"), Path.Combine(repository, "packaging", "AppxManifest.xml"));
            foreach (var asset in Directory.GetFiles(Path.Combine(RepositoryLayout.Root, "packaging", "Assets"), "*.png"))
            {
                File.Copy(asset, Path.Combine(repository, "packaging", "Assets", Path.GetFileName(asset)));
            }
            File.WriteAllText(Path.Combine(repository, "packaging", "public", ".gitkeep"), string.Empty);
            File.WriteAllText(Path.Combine(repository, "LumaTherm.sln"), string.Empty);
            var sdk = Path.Combine(repository, "fake-sdk");
            var x64 = Path.Combine(sdk, "bin", "10.0.26100.0", "x64");
            Directory.CreateDirectory(x64);
            File.WriteAllText(Path.Combine(x64, "MakeAppx.exe"), string.Empty);
            File.WriteAllText(Path.Combine(x64, "SignTool.exe"), string.Empty);
            const string password = "fixture-password-9Kx";
            using var rsa = RSA.Create(2048);
            var request = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=LumaTherm Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            var certificatePath = Path.Combine(repository, "artifacts", "fixture-signing.pfx");
            Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
            File.WriteAllBytes(certificatePath, certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, password));
            return new ReleaseFixture(root, repository, sdk, certificatePath, password);
        }

        public PowerShellResult RunBuild(params string[] arguments) =>
            PowerShellTestHost.Run(Path.Combine(RepositoryRoot, "scripts", "build-release.ps1"), arguments);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
