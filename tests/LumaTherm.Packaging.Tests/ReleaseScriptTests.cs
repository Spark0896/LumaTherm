using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace LumaTherm.Packaging.Tests;

public sealed class ReleaseScriptTests
{
    [Fact]
    public void ReleaseSignerCheckAllowsOnlyExactSelfSignedUntrustedRootAfterSignToolVerification()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"));

        Assert.Contains("function Assert-ExpectedSigner", script, StringComparison.Ordinal);
        Assert.Contains("function Assert-AuthenticodeSignature", script, StringComparison.Ordinal);
        Assert.Contains("& $SignTool verify /pa /v $Path", script, StringComparison.Ordinal);
        Assert.Contains("if ($signToolExit -ne 0 -and -not $isExpectedSelfSignedUntrustedRoot)", script, StringComparison.Ordinal);
        Assert.Contains("$signature.SignerCertificate.Thumbprint", script, StringComparison.Ordinal);
        Assert.Contains("$Certificate.Subject.Equals($Certificate.Issuer", script, StringComparison.Ordinal);
        Assert.Contains("$signature.Status -eq 'UnknownError'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("& $tools.SignTool verify /pa /v $appExe", script, StringComparison.Ordinal);
        Assert.DoesNotContain("$signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate", script, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanNamesEveryArtifactToolCommandAndForbiddenSideEffect()
    {
        var result = PowerShellTestHost.Run(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"), new[] { "-Mode", "Plan" },
            timeout: TimeSpan.FromSeconds(30), invokeViaCommand: true);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        Assert.Equal("LumaTherm-1.2.0-win-x64-setup.exe", root.GetProperty("setupName").GetString());
        Assert.Equal("LumaTherm-1.2.0-portable-win-x64.zip", root.GetProperty("portableZipName").GetString());
        Assert.Equal("LumaTherm-1.2.0-sparse.msix", root.GetProperty("sparsePackageName").GetString());
        Assert.Equal("{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}", root.GetProperty("stableAppId").GetString());
        Assert.Empty(root.GetProperty("privateKeyOutputs").EnumerateArray());
        Assert.All(root.GetProperty("forbiddenSideEffects").EnumerateObject(), property => Assert.False(property.Value.GetBoolean(), property.Name));
        Assert.Equal(new[] { "SignTool", "MakeAppx", "ISCC" }, root.GetProperty("tools").EnumerateObject().Select(property => property.Name).OrderBy(name => name).OrderByNameForTools());
        var commands = root.GetProperty("plannedCommands").EnumerateArray().Select(command => command.GetProperty("name").GetString()).ToArray();
        Assert.Equal(new[] { "restore", "test", "publish", "sign-app", "verify-app", "emit-signed-payload-anchor", "make-sparse-package", "sign-sparse-package", "verify-sparse-package", "assemble-portable", "compile-installer", "sign-installer", "verify-installer", "emit-checksums", "promote-public-artifacts" }, commands);
        var compileArguments = root.GetProperty("plannedCommands").EnumerateArray()
            .Single(command => command.GetProperty("name").GetString() == "compile-installer")
            .GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
        var testArguments = root.GetProperty("plannedCommands").EnumerateArray()
            .Single(command => command.GetProperty("name").GetString() == "test")
            .GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains(compileArguments, value => value!.StartsWith("/DPayloadRoot=", StringComparison.Ordinal));
        Assert.Contains("-m:1", testArguments);
        Assert.Empty(root.GetProperty("allowedWriteRoots").EnumerateArray());
        Assert.Empty(root.GetProperty("planWriteRoots").EnumerateArray());
        Assert.All(root.GetProperty("fullBuildWriteRoots").EnumerateArray(), value => Assert.True(Path.IsPathFullyQualified(value.GetString()!)));
        Assert.Contains(root.GetProperty("fullBuildWriteRoots").EnumerateArray(), value => value.GetString()!.EndsWith("src\\LumaTherm.App\\obj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("fullBuildWriteRoots").EnumerateArray(), value => value.GetString()!.EndsWith("tests\\LumaTherm.Packaging.Tests\\bin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(root.GetProperty("cacheWriteRoots").EnumerateArray(), value => value.GetString()!.EndsWith(".nuget\\packages", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanAcceptsExplicitPortableCompilerWithoutEnablingTestOverridesOrExecutingIt()
    {
        using var fixture = ReleaseFixture.Create();
        var result = PowerShellTestHost.Run(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"),
            new[] { "-Mode", "Plan", "-InnoSetupPath", fixture.IsccPath },
            environment: new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "0" },
            invokeViaCommand: true);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var compiler = json.RootElement.GetProperty("tools").GetProperty("ISCC");
        Assert.Equal(fixture.IsccPath, compiler.GetProperty("path").GetString());
        Assert.True(compiler.GetProperty("available").GetBoolean());
        Assert.False(json.RootElement.GetProperty("forbiddenSideEffects").GetProperty("processStart").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "dist")));
    }

    [Fact]
    public void PlanWithExternalPfxRedactsPasswordAndPrivatePath()
    {
        using var fixture = ReleaseFixture.Create();
        var result = fixture.Run("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath,
            "-CertificatePath", fixture.PfxPath, "-CertificatePassword", fixture.Password);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        Assert.DoesNotContain(fixture.Password, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.PfxPath, result.StandardOutput + result.StandardError, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("external-provided", json.RootElement.GetProperty("certificateSource").GetString());
        Assert.All(json.RootElement.GetProperty("tools").EnumerateObject(), property => Assert.True(property.Value.GetProperty("available").GetBoolean()));
        var arguments = json.RootElement.GetProperty("plannedCommands").EnumerateArray()
            .SelectMany(command => command.GetProperty("arguments").EnumerateArray()).Select(value => value.GetString()).ToArray();
        Assert.Contains("<external-pfx>", arguments);
        Assert.Contains("<secure-password>", arguments);
    }

    [Fact]
    public void PlanRejectsPfxInsideRepositoryAndInvalidPasswordWithoutLeakingSecrets()
    {
        using var fixture = ReleaseFixture.Create();
        var inside = Path.Combine(fixture.RepositoryRoot, "inside.pfx");
        File.Copy(fixture.PfxPath, inside);
        var insideResult = fixture.Run("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath,
            "-CertificatePath", inside, "-CertificatePassword", fixture.Password);
        Assert.NotEqual(0, insideResult.ExitCode);
        Assert.Contains("outside", insideResult.StandardError + insideResult.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(fixture.Password, insideResult.StandardOutput + insideResult.StandardError, StringComparison.Ordinal);

        const string wrong = "wrong-private-password";
        var passwordResult = fixture.Run("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath,
            "-CertificatePath", fixture.PfxPath, "-CertificatePassword", wrong);
        Assert.NotEqual(0, passwordResult.ExitCode);
        Assert.Contains("password", passwordResult.StandardError + passwordResult.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(wrong, passwordResult.StandardOutput + passwordResult.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void FullFailsClosedBeforeWritesWhenPfxOrAnyExternalToolIsMissing()
    {
        using var fixture = ReleaseFixture.Create();
        var missingPfx = fixture.Run("-Mode", "Full", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath);
        Assert.NotEqual(0, missingPfx.ExitCode);
        Assert.Contains("PFX", missingPfx.StandardError + missingPfx.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "dist")));

        File.Delete(fixture.IsccPath);
        var missingTool = fixture.Run("-Mode", "Full", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath,
            "-CertificatePath", fixture.PfxPath, "-CertificatePassword", fixture.Password);
        Assert.NotEqual(0, missingTool.ExitCode);
        Assert.Contains("ISCC", missingTool.StandardError + missingTool.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "dist")));
    }

    [Fact]
    public void FullRejectsPublisherCertificateMismatchBeforeAnyToolOrPublicWrite()
    {
        using var fixture = ReleaseFixture.Create();

        var result = fixture.Run("-Mode", "Full", "-SdkBuildToolsPath", fixture.SdkRoot,
            "-InnoSetupPath", fixture.IsccPath, "-CertificatePath", fixture.PfxPath,
            "-CertificatePassword", fixture.Password, "-Publisher", "CN=Unexpected Publisher");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Publisher does not match", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid argument/option", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "artifacts")));
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "dist")));
    }

    [Fact]
    public void PortableAssemblyContainsAppIdentityCertificateHelpersDocsAndInternalChecksums()
    {
        using var fixture = ReleaseFixture.Create();
        var publish = Path.Combine(fixture.Root, "published");
        Directory.CreateDirectory(publish);
        File.WriteAllText(Path.Combine(publish, "LumaTherm.exe"), "signed app");
        var sparse = Path.Combine(fixture.Root, "LumaTherm-1.2.0-sparse.msix");
        File.WriteAllText(sparse, "signed sparse package");
        var certificate = Path.Combine(fixture.Root, "LumaTherm.cer");
        File.WriteAllText(certificate, "public cert");

        var first = fixture.Run("-Mode", "AssemblePortable", "-PublishedAppPath", publish, "-SparsePackagePath", sparse, "-CertificatePublicPath", certificate);
        Assert.Equal(0, first.ExitCode);
        var zip = Path.Combine(fixture.RepositoryRoot, "dist", "LumaTherm-1.2.0-portable-win-x64.zip");
        var firstHash = SHA256.HashData(File.ReadAllBytes(zip));
        using (var archive = ZipFile.OpenRead(zip))
        {
            var names = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.Equal(new[] { "LICENSE", "LumaTherm-1.2.0-sparse.msix", "LumaTherm.cer", "README.md", "Register-LumaTherm.cmd", "Register-LumaTherm.ps1", "SHA256SUMS.txt", "Unregister-LumaTherm.ps1", "app/LumaTherm.exe" }, names);
        }

        var second = fixture.Run("-Mode", "AssemblePortable", "-PublishedAppPath", publish, "-SparsePackagePath", sparse, "-CertificatePublicPath", certificate);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(firstHash, SHA256.HashData(File.ReadAllBytes(zip)));
    }
    [Fact]
    public void PortableAssemblyRejectsUnexpectedPublishOutputsBeforeCreatingZip()
    {
        using var fixture = ReleaseFixture.Create();
        var publish = Path.Combine(fixture.Root, "published-extra");
        Directory.CreateDirectory(publish);
        File.WriteAllText(Path.Combine(publish, "LumaTherm.exe"), "signed app");
        File.WriteAllText(Path.Combine(publish, "unexpected.dll"), "not anchored");
        var sparse = Path.Combine(fixture.Root, "LumaTherm-1.2.0-sparse.msix");
        File.WriteAllText(sparse, "signed sparse package");
        var certificate = Path.Combine(fixture.Root, "LumaTherm.cer");
        File.WriteAllText(certificate, "public cert");

        var result = fixture.Run("-Mode", "AssemblePortable", "-PublishedAppPath", publish, "-SparsePackagePath", sparse, "-CertificatePublicPath", certificate);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("unexpected published", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "dist", "LumaTherm-1.2.0-portable-win-x64.zip")));
    }


    [Fact]
    public void PublicChecksumsCoverExactlySetupAndPortableArtifactsInStableOrder()
    {
        using var fixture = ReleaseFixture.Create();
        var dist = Path.Combine(fixture.RepositoryRoot, "dist");
        Directory.CreateDirectory(dist);
        File.WriteAllText(Path.Combine(dist, "LumaTherm-1.2.0-win-x64-setup.exe"), "setup");
        File.WriteAllText(Path.Combine(dist, "LumaTherm-1.2.0-portable-win-x64.zip"), "zip");
        var result = fixture.Run("-Mode", "EmitChecksums");
        Assert.Equal(0, result.ExitCode);
        var lines = File.ReadAllLines(Path.Combine(dist, "SHA256SUMS.txt"));
        Assert.Equal(2, lines.Length);
        Assert.Equal(new[] { "LumaTherm-1.2.0-portable-win-x64.zip", "LumaTherm-1.2.0-win-x64-setup.exe" }, lines.Select(line => line[(line.IndexOf(" *", StringComparison.Ordinal) + 2)..]).ToArray());
        Assert.All(lines, line => Assert.Matches("^[0-9A-F]{64} \\*[^\\\\/]+$", line));
    }

    private sealed class ReleaseFixture : IDisposable
    {
        private ReleaseFixture(string root, string repositoryRoot, string sdkRoot, string isccPath, string pfxPath, string password)
        {
            Root = root; RepositoryRoot = repositoryRoot; SdkRoot = sdkRoot; IsccPath = isccPath; PfxPath = pfxPath; Password = password;
        }
        public string Root { get; }
        public string RepositoryRoot { get; }
        public string SdkRoot { get; }
        public string IsccPath { get; }
        public string PfxPath { get; }
        public string Password { get; }

        public static ReleaseFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "LumaTherm-release-tests", Guid.NewGuid().ToString("N"));
            var repository = Path.Combine(root, "repository");
            Directory.CreateDirectory(Path.Combine(repository, "scripts"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "sparse"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "Assets"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "public"));
            Directory.CreateDirectory(Path.Combine(repository, "src", "LumaTherm.App"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"), Path.Combine(repository, "scripts", "build-release.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.cmd"), Path.Combine(repository, "scripts", "Register-LumaTherm.cmd"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.ps1"), Path.Combine(repository, "scripts", "Register-LumaTherm.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Unregister-LumaTherm.ps1"), Path.Combine(repository, "scripts", "Unregister-LumaTherm.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "packaging", "LumaTherm.iss"), Path.Combine(repository, "packaging", "LumaTherm.iss"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml"), Path.Combine(repository, "packaging", "sparse", "AppxManifest.xml"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "README.md"), Path.Combine(repository, "README.md"));
            File.WriteAllText(Path.Combine(repository, "LICENSE"), "fixture license terms");
            File.WriteAllText(Path.Combine(repository, "LumaTherm.sln"), "fixture");

            var sdkRoot = Path.Combine(root, "sdk");
            var toolDir = Path.Combine(sdkRoot, "bin", "10.0.26100.0", "x64");
            Directory.CreateDirectory(toolDir);
            File.WriteAllText(Path.Combine(toolDir, "SignTool.exe"), "fake");
            File.WriteAllText(Path.Combine(toolDir, "MakeAppx.exe"), "fake");
            var isccPath = Path.Combine(root, "inno", "ISCC.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(isccPath)!);
            File.WriteAllText(isccPath, "fake");

            var dotnetPath = Path.Combine(repository, ".dotnet", "dotnet.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(dotnetPath)!);
            File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "whoami.exe"), dotnetPath);

            const string password = "fixture-private-password";
            var pfxPath = Path.Combine(root, "secrets", "release-signing.pfx");
            Directory.CreateDirectory(Path.GetDirectoryName(pfxPath)!);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LumaTherm Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(pfxPath, cert.Export(X509ContentType.Pfx, password));
            return new ReleaseFixture(root, repository, sdkRoot, isccPath, pfxPath, password);
        }

        public PowerShellResult Run(params string[] arguments)
        {
            var allArguments = arguments.Concat(new[] { "-RepositoryRootForTest", RepositoryRoot }).ToArray();
            return PowerShellTestHost.Run(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"), allArguments,
                new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" }, TimeSpan.FromSeconds(30), invokeViaCommand: true);
        }

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}

internal static class ToolNameOrdering
{
    public static string?[] OrderByNameForTools(this IEnumerable<string?> names)
    {
        var desired = new[] { "SignTool", "MakeAppx", "ISCC" };
        return names.OrderBy(name => Array.IndexOf(desired, name)).ToArray();
    }
}
