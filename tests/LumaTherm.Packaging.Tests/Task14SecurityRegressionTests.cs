using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace LumaTherm.Packaging.Tests;

public sealed class Task14SecurityRegressionTests
{
    [Fact]
    public void RewrittenChecksumsCannotAuthorizeAReplacedExecutable()
    {
        using var fixture = SecurePortableFixture.Create();
        File.WriteAllText(fixture.ApplicationPath, "attacker replacement");
        fixture.RewriteChecksums();

        var result = fixture.Register();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("signed payload anchor", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registrationPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void ExecutableSignerMustMatchTheBundledCertificate()
    {
        using var fixture = SecurePortableFixture.Create();
        var result = fixture.Register("-ApplicationSignatureThumbprintForTest", new string('A', 40));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("executable signer", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registrationPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedAnchorMustExactlyCoverExternalPortableContent()
    {
        using var changed = SecurePortableFixture.Create();
        File.WriteAllText(Path.Combine(changed.Root, "README.md"), "rewritten documentation");
        changed.RewriteChecksums();
        var mismatch = changed.Register();
        Assert.NotEqual(0, mismatch.ExitCode);
        Assert.Contains("signed payload anchor", mismatch.StandardError + mismatch.StandardOutput, StringComparison.OrdinalIgnoreCase);

        using var extra = SecurePortableFixture.Create();
        File.WriteAllText(Path.Combine(extra.Root, "extra.cmd"), "unexpected executable content");
        extra.RewriteChecksums();
        var unexpected = extra.Register();
        Assert.NotEqual(0, unexpected.ExitCode);
        Assert.Contains("anchor", unexpected.StandardError + unexpected.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registrationPlanned", unexpected.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidApplicationSignatureFailsBeforeRegistration()
    {
        using var fixture = SecurePortableFixture.Create();
        var result = fixture.Register("-ApplicationSignatureStatusForTest", "NotSigned");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("executable Authenticode", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registrationPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void SilentCertificateImportRequiresExplicitCallerOptIn()
    {
        using var fixture = SecurePortableFixture.Create();
        var refused = fixture.Register("-SignatureStatusForTest", "NotTrusted", "-AdministratorStatusForTest", "Admin",
            "-NonInteractive", "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid");
        Assert.Equal(3, refused.ExitCode);
        Assert.DoesNotContain("certificateImportPlanned", refused.StandardOutput, StringComparison.Ordinal);

        var accepted = fixture.Register("-SignatureStatusForTest", "NotTrusted", "-AdministratorStatusForTest", "Admin",
            "-NonInteractive", "-ConfirmCertificateImport", "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid");
        Assert.True(accepted.ExitCode == 0, accepted.StandardError + accepted.StandardOutput);
        Assert.Contains("certificateImportConfirmed", accepted.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactInstalledIdentityAndLightingContractAreVerifiedAfterRegistration()
    {
        using var fixture = SecurePortableFixture.Create();
        var result = fixture.Register(fixture.ExactPostRegistrationArguments());

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Contains("registrationExecutedForTest", json.RootElement.GetProperty("events").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("postRegistrationVerified", json.RootElement.GetProperty("events").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void ExactSingleSameVersionIdentityIsRemovedImmediatelyBeforeRegistration()
    {
        using var fixture = SecurePortableFixture.Create();
        const string fullName = "LumaTherm_1.2.0.0_x64__exact";
        var inventory = JsonSerializer.Serialize(new[]
        {
            new { Name = "LumaTherm", Publisher = "CN=LumaTherm Local", Version = "1.2.0.0", PackageFullName = fullName },
        });
        var arguments = fixture.ExactPostRegistrationArguments().Concat(new[] { "-PreRegistrationPackagesJsonForTest", inventory }).ToArray();

        var result = fixture.Register(arguments);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.True(Array.IndexOf(events, "sameVersionPackageRemoved") < Array.IndexOf(events, "registrationExecutedForTest"));
        Assert.Equal(new[] { "Remove-AppxPackage", "-Package", fullName },
            json.RootElement.GetProperty("sameVersionRemovalCommand").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"Name\":\"LumaTherm\",\"Publisher\":\"CN=LumaTherm Local\",\"Version\":\"1.0.1.0\",\"PackageFullName\":\"older\"}]")]
    [InlineData("[{\"Name\":\"LumaTherm\",\"Publisher\":\"CN=Other\",\"Version\":\"1.2.0.0\",\"PackageFullName\":\"otherPublisher\"}]")]
    [InlineData("[{\"Name\":\"LumaTherm.Helper\",\"Publisher\":\"CN=LumaTherm Local\",\"Version\":\"1.2.0.0\",\"PackageFullName\":\"lookalike\"}]")]
    [InlineData("[{\"Name\":\"LumaTherm\",\"Publisher\":\"CN=LumaTherm Local\",\"Version\":\"1.2.0.0\",\"PackageFullName\":\"one\"},{\"Name\":\"LumaTherm\",\"Publisher\":\"CN=LumaTherm Local\",\"Version\":\"1.2.0.0\",\"PackageFullName\":\"two\"}]")]
    public void FreshUpgradeUnknownAndAmbiguousInventoriesRemoveNothing(string inventory)
    {
        using var fixture = SecurePortableFixture.Create();
        var arguments = fixture.ExactPostRegistrationArguments().Concat(new[] { "-PreRegistrationPackagesJsonForTest", inventory }).ToArray();

        var result = fixture.Register(arguments);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        Assert.DoesNotContain("sameVersionPackageRemoved", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-AppxPackage", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedSameVersionReplacementReportsUnrecoverableIdentityLossWithoutRollbackClaim()
    {
        using var fixture = SecurePortableFixture.Create();
        var inventory = JsonSerializer.Serialize(new[]
        {
            new { Name = "LumaTherm", Publisher = "CN=LumaTherm Local", Version = "1.2.0.0", PackageFullName = "LumaTherm_1.2.0.0_x64__exact" },
        });
        var arguments = fixture.ExactPostRegistrationArguments().Concat(new[]
        {
            "-PreRegistrationPackagesJsonForTest", inventory,
            "-RegistrationFailureForTest", "Add",
        }).ToArray();

        var result = fixture.Register(arguments);

        Assert.NotEqual(0, result.ExitCode);
        var diagnostic = result.StandardError + result.StandardOutput;
        Assert.Contains("sameVersionPackageRemoved", diagnostic, StringComparison.Ordinal);
        Assert.Contains("sameVersionReplacementFailed", diagnostic, StringComparison.Ordinal);
        Assert.Contains("previous identity is no longer installed and cannot be rolled back safely", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packageRollback", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("-InstalledPackageNameForTest", "LumaTherm.Lookalike")]
    [InlineData("-InstalledPublisherForTest", "CN=Other")]
    [InlineData("-InstalledVersionForTest", "1.2.0.1")]
    [InlineData("-InstalledExternalLocationForTest", "C:\\elsewhere")]
    [InlineData("-InstalledApplicationIdForTest", "OtherApp")]
    [InlineData("-InstalledExtensionForTest", "com.example.unrelated")]
    public void PostRegistrationMismatchRollsBackExactPackage(string key, string value)
    {
        using var fixture = SecurePortableFixture.Create();
        var arguments = fixture.ExactPostRegistrationArguments().ToList();
        arguments[Array.FindIndex(arguments.ToArray(), item => item.Equals(key, StringComparison.OrdinalIgnoreCase)) + 1] = value;

        var result = fixture.Register(arguments.ToArray());

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("packageRollbackPlanned", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("postRegistrationVerified", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationFailureRemovesOnlyNewlyImportedTrust()
    {
        using var fixture = SecurePortableFixture.Create();
        var failure = fixture.Register("-SignatureStatusForTest", "NotTrusted", "-AdministratorStatusForTest", "Admin",
            "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid", "-SimulateRegistrationForTest",
            "-RegistrationFailureForTest", "Add");
        Assert.NotEqual(0, failure.ExitCode);
        Assert.Contains("certificateRollbackPlanned", failure.StandardError + failure.StandardOutput, StringComparison.Ordinal);

        var preexisting = fixture.Register("-SignatureStatusForTest", "NotTrusted", "-AdministratorStatusForTest", "Admin",
            "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid", "-TrustedCertificatePresentForTest",
            "-SimulateRegistrationForTest", "-RegistrationFailureForTest", "Add");
        Assert.NotEqual(0, preexisting.ExitCode);
        Assert.DoesNotContain("certificateRollbackPlanned", preexisting.StandardError + preexisting.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableRootWithAReparseAncestorIsRejected()
    {
        using var fixture = SecurePortableFixture.Create();
        var link = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "portable-link-" + Guid.NewGuid().ToString("N"));
        if (!TryCreateJunction(link, fixture.Root)) Assert.Skip("Junction creation is unavailable on this Windows host.");
        try
        {
            var nested = Path.Combine(link, "nested");
            Directory.CreateDirectory(Path.Combine(fixture.Root, "nested"));
            var result = fixture.RegisterWithPortableRoot(nested);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("registrationPlanned", result.StandardOutput, StringComparison.Ordinal);
        }
        finally { Directory.Delete(link); }
    }

    [Fact]
    public void AmbiguousUnregisterInputRemovesNothing()
    {
        using var fixture = SecurePortableFixture.Create();
        const string packages = "[{\"Name\":\"LumaTherm\",\"Publisher\":\"CN=LumaTherm Local\",\"PackageFullName\":\"one\"},{\"Name\":\"LumaTherm\",\"Publisher\":\"CN=LumaTherm Local\",\"PackageFullName\":\"two\"}]";
        var result = fixture.Unregister("-AuditOnly", "-Force", "-InstalledPackagesJsonForTest", packages);

        Assert.True(result.ExitCode != 0, result.StandardError + result.StandardOutput);
        Assert.Contains("ambiguous", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packageRemovalPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanUsesFilesystemOnlyDiscoveryAndLeavesFixtureUnchanged()
    {
        using var fixture = ReleaseSecurityFixture.Create();
        var before = fixture.Snapshot();
        var result = fixture.Run("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath);
        var after = fixture.Snapshot();

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(before, after);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal("filesystem-only", json.RootElement.GetProperty("toolDiscoveryStrategy").GetString());
        Assert.Empty(json.RootElement.GetProperty("planWriteRoots").EnumerateArray());
        Assert.NotEmpty(json.RootElement.GetProperty("fullBuildWriteRoots").EnumerateArray());
    }

    [Theory]
    [InlineData("Compile")]
    [InlineData("Sign")]
    [InlineData("Verify")]
    [InlineData("Checksums")]
    public void DownstreamFailureLeavesNoReleasablePartialArtifacts(string failurePoint)
    {
        using var fixture = ReleaseSecurityFixture.Create();
        Directory.CreateDirectory(Path.Combine(fixture.RepositoryRoot, "dist"));
        File.WriteAllText(Path.Combine(fixture.RepositoryRoot, "dist", "keep.txt"), "prior release marker");

        var result = fixture.Run("-Mode", "TestTransaction", "-FailurePointForTest", failurePoint);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains($"Injected {failurePoint} failure", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
        Assert.Equal("prior release marker", File.ReadAllText(Path.Combine(fixture.RepositoryRoot, "dist", "keep.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "dist", "LumaTherm-1.2.0-win-x64-setup.exe")));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "dist", "LumaTherm-1.2.0-portable-win-x64.zip")));
        Assert.False(File.Exists(Path.Combine(fixture.RepositoryRoot, "dist", "SHA256SUMS.txt")));
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "artifacts", "release", "public-staging")));
    }

    [Fact]
    public void ValidatedArtifactsArePromotedTogether()
    {
        using var fixture = ReleaseSecurityFixture.Create();
        var result = fixture.Run("-Mode", "TestTransaction", "-FailurePointForTest", "None");

        Assert.Equal(0, result.ExitCode);
        var names = Directory.GetFiles(Path.Combine(fixture.RepositoryRoot, "dist")).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "LumaTherm-1.2.0-portable-win-x64.zip", "LumaTherm-1.2.0-win-x64-setup.exe", "SHA256SUMS.txt" }, names);
    }

    [Fact]
    public void ReleaseTreeJunctionIsRejectedWithoutTouchingExternalContent()
    {
        using var fixture = ReleaseSecurityFixture.Create();
        var external = Path.Combine(fixture.Root, "external-artifacts");
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "sentinel.txt");
        File.WriteAllText(sentinel, "preserve");
        var artifacts = Path.Combine(fixture.RepositoryRoot, "artifacts");
        if (!TryCreateJunction(artifacts, external)) Assert.Skip("Junction creation is unavailable on this Windows host.");
        try
        {
            var result = fixture.Run("-Mode", "TestTransaction", "-FailurePointForTest", "None");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("preserve", File.ReadAllText(sentinel));
        }
        finally { Directory.Delete(artifacts); }
    }

    [Fact]
    public void PfxPathThroughJunctionIntoRepositoryIsRejected()
    {
        using var fixture = ReleaseSecurityFixture.Create();
        var insideDirectory = Path.Combine(fixture.RepositoryRoot, "private");
        Directory.CreateDirectory(insideDirectory);
        var insidePfx = Path.Combine(insideDirectory, "inside.pfx");
        File.Copy(fixture.PfxPath, insidePfx);
        var link = Path.Combine(fixture.Root, "pfx-link");
        if (!TryCreateJunction(link, insideDirectory)) Assert.Skip("Junction creation is unavailable on this Windows host.");
        try
        {
            var result = fixture.Run("-Mode", "Plan", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath,
                "-CertificatePath", Path.Combine(link, "inside.pfx"), "-CertificatePassword", fixture.Password);
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fixture.Password, result.StandardOutput + result.StandardError, StringComparison.Ordinal);
        }
        finally { Directory.Delete(link); }
    }

    [Theory]
    [InlineData("Reverify")]
    [InlineData("Add")]
    [InlineData("PostVerify")]
    public void NewlyImportedTrustIsRolledBackAtEveryRegistrationFailureBoundary(string failurePoint)
    {
        using var fixture = SecurePortableFixture.Create();
        var arguments = new List<string>
        {
            "-SignatureStatusForTest", "NotTrusted", "-AdministratorStatusForTest", "Admin",
            "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid"
        };
        if (failurePoint == "Reverify")
        {
            arguments[arguments.IndexOf("Valid")] = "Invalid";
        }
        else
        {
            arguments.AddRange(fixture.ExactPostRegistrationArguments());
            arguments.AddRange(new[] { "-RegistrationFailureForTest", failurePoint });
        }

        var result = fixture.Register(arguments.ToArray());

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("certificateRollbackPlanned", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
        if (failurePoint == "PostVerify") Assert.Contains("packageRollbackPlanned", result.StandardError + result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SignTool.exe", "SignTool")]
    [InlineData("MakeAppx.exe", "MakeAppx")]
    public void FullFailsClosedBeforeWritesWhenAnSdkToolIsMissing(string fileName, string toolName)
    {
        using var fixture = ReleaseSecurityFixture.Create();
        File.Delete(Path.Combine(fixture.SdkRoot, "bin", "10.0.26100.0", "x64", fileName));

        var result = fixture.Run("-Mode", "Full", "-SdkBuildToolsPath", fixture.SdkRoot, "-InnoSetupPath", fixture.IsccPath,
            "-CertificatePath", fixture.PfxPath, "-CertificatePassword", fixture.Password);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(toolName, result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "dist")));
        Assert.False(Directory.Exists(Path.Combine(fixture.RepositoryRoot, "artifacts", "release")));
    }

    [Theory]
    [InlineData(false, "Default", null, false)]
    [InlineData(false, "Decline", null, false)]
    [InlineData(false, "Accept", null, true)]
    [InlineData(true, "Accept", null, false)]
    [InlineData(true, "Default", "/ALLOWCERTIMPORT", true)]
    [InlineData(true, "Default", "/allowcertimport", true)]
    [InlineData(true, "Accept", "/ALLOWCERTIMPORT=1", false)]
    [InlineData(true, "Accept", " /ALLOWCERTIMPORT", false)]
    public void InstallerConsentPolicyRequiresVisibleAcceptanceOrExactSilentOptIn(
        bool wizardSilent, string visibleDecision, string? installerArgument, bool expectedApproved)
    {
        var arguments = new List<string> { "-Mode", "Evaluate", "-VisibleDecision", visibleDecision };
        if (wizardSilent) { arguments.Add("-WizardSilent"); }
        if (installerArgument is not null) { arguments.AddRange(new[] { "-InstallerArgument", installerArgument }); }

        var result = PowerShellTestHost.Run(
            Path.Combine(RepositoryLayout.Root, "scripts", "Resolve-LumaThermInstallerConsent.ps1"), arguments,
            timeout: TimeSpan.FromSeconds(15));

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(expectedApproved, json.RootElement.GetProperty("approved").GetBoolean());
        const string approvedArguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{app}\\payload\\Register-LumaTherm.ps1\" -PortableDirectory \"{app}\\payload\" -NonInteractive -ConfirmCertificateImport";
        Assert.Equal(expectedApproved ? approvedArguments : string.Empty,
            json.RootElement.GetProperty("registrationArguments").GetString());
    }

    [Fact]
    public void GeneratedInnoConsentBoundaryMatchesTheCommittedInclude()
    {
        var root = Path.Combine(Path.GetTempPath(), "LumaTherm-consent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var generated = Path.Combine(root, "LumaTherm.Consent.iss");
            var result = PowerShellTestHost.Run(
                Path.Combine(RepositoryLayout.Root, "scripts", "Resolve-LumaThermInstallerConsent.ps1"),
                new[] { "-Mode", "GenerateInno", "-OutputPath", generated }, timeout: TimeSpan.FromSeconds(15));

            Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
            var committedText = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "packaging", "LumaTherm.Consent.iss"))
                .Replace("\r\n", "\n", StringComparison.Ordinal);
            var generatedText = File.ReadAllText(generated).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.Equal(committedText, generatedText);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static bool TryCreateJunction(string junction, string target)
    {
        var result = BoundedProcessTestHost.Run(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", junction, target },
        }, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        return result.ExitCode == 0;
    }

    private sealed class SecurePortableFixture : IDisposable
    {
        private SecurePortableFixture(string root, string thumbprint) { Root = root; Thumbprint = thumbprint; }
        public string Root { get; }
        public string Thumbprint { get; }
        public string ApplicationPath => Path.Combine(Root, "app", "LumaTherm.exe");
        public string ApplicationDirectory => Path.Combine(Root, "app");
        public string PackagePath => Path.Combine(Root, "LumaTherm-1.2.0-sparse.msix");

        public static SecurePortableFixture Create()
        {
            var root = Path.Combine(RepositoryLayout.Root, "artifacts", "packaging-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "app"));
            File.WriteAllText(Path.Combine(root, "app", "LumaTherm.exe"), "signed app");
            File.WriteAllText(Path.Combine(root, "README.md"), "readme");
            File.WriteAllText(Path.Combine(root, "LICENSE"), "license");
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.cmd"), Path.Combine(root, "Register-LumaTherm.cmd"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.ps1"), Path.Combine(root, "Register-LumaTherm.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Unregister-LumaTherm.ps1"), Path.Combine(root, "Unregister-LumaTherm.ps1"));
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LumaTherm Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(Path.Combine(root, "LumaTherm.cer"), certificate.Export(X509ContentType.Cert));
            WriteAnchorPackage(root);
            RewriteChecksums(root);
            return new SecurePortableFixture(root, certificate.Thumbprint);
        }

        public string[] ExactPostRegistrationArguments() => new[]
        {
            "-SimulateRegistrationForTest", "-InstalledPackageNameForTest", "LumaTherm", "-InstalledPublisherForTest", "CN=LumaTherm Local",
            "-InstalledVersionForTest", "1.2.0.0", "-InstalledExternalLocationForTest", ApplicationDirectory,
            "-InstalledApplicationIdForTest", "LumaTherm", "-InstalledExtensionForTest", "com.microsoft.windows.lighting"
        };

        public PowerShellResult Register(params string[] arguments) => RegisterWithPortableRoot(Root, arguments);

        public PowerShellResult RegisterWithPortableRoot(string portableRoot, params string[] arguments)
        {
            var all = arguments.ToList();
            AddDefault(all, "-AuditOnly", null);
            AddDefault(all, "-PortableDirectory", portableRoot);
            AddDefault(all, "-SignatureStatusForTest", "Valid");
            AddDefault(all, "-SignatureThumbprintForTest", Thumbprint);
            AddDefault(all, "-ApplicationSignatureStatusForTest", "Valid");
            AddDefault(all, "-ApplicationSignatureThumbprintForTest", Thumbprint);
            return PowerShellTestHost.Run(Path.Combine(Root, "Register-LumaTherm.ps1"), all,
                new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" });
        }

        public PowerShellResult Unregister(params string[] arguments) => PowerShellTestHost.Run(
            Path.Combine(Root, "Unregister-LumaTherm.ps1"), arguments,
            new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" });

        public void RewriteChecksums() => RewriteChecksums(Root);

        private static void AddDefault(List<string> arguments, string key, string? value)
        {
            if (arguments.Contains(key, StringComparer.OrdinalIgnoreCase)) return;
            arguments.Add(key);
            if (value is not null) arguments.Add(value);
        }

        private static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        private static void WriteAnchorPackage(string root)
        {
            var names = new[] { "app/LumaTherm.exe", "LICENSE", "LumaTherm.cer", "README.md", "Register-LumaTherm.cmd", "Register-LumaTherm.ps1", "Unregister-LumaTherm.ps1" };
            var anchor = JsonSerializer.Serialize(new
            {
                version = 1,
                files = names.Select(name => new { path = name, sha256 = Sha(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar))) }).ToArray()
            });
            using var archive = ZipFile.Open(Path.Combine(root, "LumaTherm-1.2.0-sparse.msix"), ZipArchiveMode.Create);
            var payload = archive.CreateEntry("PayloadHashes.json");
            using (var writer = new StreamWriter(payload.Open(), new UTF8Encoding(false))) writer.Write(anchor);
            var manifest = archive.CreateEntry("AppxManifest.xml");
            using var manifestWriter = new StreamWriter(manifest.Open(), new UTF8Encoding(false));
            manifestWriter.Write("<Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Identity Name=\"LumaTherm\" Publisher=\"CN=LumaTherm Local\" Version=\"1.2.0.0\" /></Package>");
        }

        private static void RewriteChecksums(string root)
        {
            var checksum = Path.Combine(root, "SHA256SUMS.txt");
            var names = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.Equals(checksum, StringComparison.OrdinalIgnoreCase))
                .Select(path => path[(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1)..].Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            File.WriteAllLines(checksum, names.Select(name => $"{Sha(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)))} *{name}"), new UTF8Encoding(false));
        }

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    private sealed class ReleaseSecurityFixture : IDisposable
    {
        private ReleaseSecurityFixture(string root, string repositoryRoot, string sdkRoot, string isccPath, string pfxPath, string password)
        { Root = root; RepositoryRoot = repositoryRoot; SdkRoot = sdkRoot; IsccPath = isccPath; PfxPath = pfxPath; Password = password; }
        public string Root { get; }
        public string RepositoryRoot { get; }
        public string SdkRoot { get; }
        public string IsccPath { get; }
        public string PfxPath { get; }
        public string Password { get; }

        public static ReleaseSecurityFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "LumaTherm-security-tests", Guid.NewGuid().ToString("N"));
            var repository = Path.Combine(root, "repository");
            Directory.CreateDirectory(Path.Combine(repository, "scripts"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "sparse"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "Assets"));
            Directory.CreateDirectory(Path.Combine(repository, "packaging", "public"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"), Path.Combine(repository, "scripts", "build-release.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.cmd"), Path.Combine(repository, "scripts", "Register-LumaTherm.cmd"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.ps1"), Path.Combine(repository, "scripts", "Register-LumaTherm.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Unregister-LumaTherm.ps1"), Path.Combine(repository, "scripts", "Unregister-LumaTherm.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "packaging", "LumaTherm.iss"), Path.Combine(repository, "packaging", "LumaTherm.iss"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml"), Path.Combine(repository, "packaging", "sparse", "AppxManifest.xml"));
            File.WriteAllText(Path.Combine(repository, "README.md"), "readme");
            File.WriteAllText(Path.Combine(repository, "LICENSE"), "license");
            File.WriteAllText(Path.Combine(repository, "LumaTherm.sln"), "fixture");
            var sdkRoot = Path.Combine(root, "sdk");
            var tools = Path.Combine(sdkRoot, "bin", "10.0.26100.0", "x64");
            Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(tools, "SignTool.exe"), "fake");
            File.WriteAllText(Path.Combine(tools, "MakeAppx.exe"), "fake");
            var iscc = Path.Combine(root, "inno", "ISCC.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(iscc)!);
            File.WriteAllText(iscc, "fake");
            const string password = "fixture-secret";
            var pfx = Path.Combine(root, "secrets", "signing.pfx");
            Directory.CreateDirectory(Path.GetDirectoryName(pfx)!);
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LumaTherm Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(pfx, cert.Export(X509ContentType.Pfx, password));
            return new ReleaseSecurityFixture(root, repository, sdkRoot, iscc, pfx, password);
        }

        public string[] Snapshot() => Directory.GetFileSystemEntries(RepositoryRoot, "*", SearchOption.AllDirectories)
            .Select(path => path[(RepositoryRoot.Length + 1)..] + "|" + (File.Exists(path) ? new FileInfo(path).Length : -1))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();

        public PowerShellResult Run(params string[] arguments)
        {
            var all = arguments.Concat(new[] { "-RepositoryRootForTest", RepositoryRoot }).ToArray();
            return PowerShellTestHost.Run(Path.Combine(RepositoryLayout.Root, "scripts", "build-release.ps1"), all,
                new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" }, TimeSpan.FromSeconds(30), invokeViaCommand: true);
        }

        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }
}
