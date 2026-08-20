using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace LumaTherm.Packaging.Tests;

public sealed class InstallerScriptTests
{
    [Fact]
    public void AuditAcceptsMatchingNotTrustedSignatureOnlyAfterPlannedTrustAndReverification()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall(
            "-AuditOnly", "-CertificateDecisionForTest", "Accept",
            "-SignatureStatusForTest", "NotTrusted",
            "-ReverifiedSignatureStatusForTest", "Valid",
            "-SignatureThumbprintForTest", fixture.CertificateThumbprint);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[]
        {
            "checksumVerified", "signatureMatchedUntrusted", "certificateImportConfirmed",
            "certificateImportPlanned", "signatureReverificationPlanned", "signatureReverified",
            "packageInstallPlanned", "certificateTrustRetentionPlanned",
        }, events);
    }

    [Fact]
    public void AuditPlansOwnedTrustCleanupAfterReverificationThrows()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall(
            "-AuditOnly", "-CertificateDecisionForTest", "Accept",
            "-SignatureStatusForTest", "NotTrusted",
            "-ReverifiedSignatureStatusForTest", "Exception",
            "-SignatureThumbprintForTest", fixture.CertificateThumbprint);

        Assert.NotEqual(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[]
        {
            "checksumVerified", "signatureMatchedUntrusted", "certificateImportConfirmed",
            "certificateImportPlanned", "signatureReverificationPlanned",
            "signatureReverificationFailed", "certificateTrustCleanupPlanned",
        }, events);
        Assert.DoesNotContain("packageInstallPlanned", events);
    }

    [Fact]
    public void AuditPlansOwnedTrustCleanupAfterPackageInstallFailure()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall(
            "-AuditOnly", "-CertificateDecisionForTest", "Accept",
            "-SignatureStatusForTest", "NotTrusted",
            "-ReverifiedSignatureStatusForTest", "Valid",
            "-SignatureThumbprintForTest", fixture.CertificateThumbprint,
            "-InstallOutcomeForTest", "Failure");

        Assert.NotEqual(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[]
        {
            "checksumVerified", "signatureMatchedUntrusted", "certificateImportConfirmed",
            "certificateImportPlanned", "signatureReverificationPlanned", "signatureReverified",
            "packageInstallPlanned", "packageInstallFailed", "certificateTrustCleanupPlanned",
        }, events);
    }

    [Fact]
    public void AuditNeverPlansCleanupForPreExistingTrust()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall(
            "-AuditOnly", "-CertificateDecisionForTest", "Accept",
            "-SignatureStatusForTest", "NotTrusted",
            "-ReverifiedSignatureStatusForTest", "Exception",
            "-SignatureThumbprintForTest", fixture.CertificateThumbprint,
            "-TrustedCertificatePresentForTest");

        Assert.NotEqual(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("certificateAlreadyPresent", events);
        Assert.DoesNotContain("certificateTrustCleanupPlanned", events);
    }

    [Fact]
    public void AuditPerformsChecksumAndSignatureBeforeCertificateImportAndInstall()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall("-AuditOnly", "-CertificateDecisionForTest", "Accept", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", fixture.CertificateThumbprint);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[] { "checksumVerified", "signatureVerified", "packageInstallPlanned" }, events);
        Assert.DoesNotContain(events, e => e!.Contains("autostart", StringComparison.OrdinalIgnoreCase) || e.Contains("Run", StringComparison.Ordinal));
        Assert.Equal(Path.Combine(fixture.Directory, "LumaTherm-1.0.0-win-x64.msix"), json.RootElement.GetProperty("packagePath").GetString());
    }

    [Fact]
    public void AuditStopsBeforeMutationPlanWhenChecksumIsInvalid()
    {
        using var fixture = DistributionFixture.Create();
        File.AppendAllText(Path.Combine(fixture.Directory, "LumaTherm-1.0.0-win-x64.msix"), "tampered");

        var result = fixture.RunInstall("-AuditOnly", "-CertificateDecisionForTest", "Accept", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", fixture.CertificateThumbprint);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("checksum", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packageInstallPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditHonorsCertificateImportDeclineWithoutInstalling()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall("-AuditOnly", "-CertificateDecisionForTest", "Decline", "-SignatureStatusForTest", "NotTrusted", "-ReverifiedSignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", fixture.CertificateThumbprint);

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("certificateImportDeclined", result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("packageInstallPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditRejectsSignatureWhoseSignerDoesNotMatchSiblingCertificate()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunInstall("-AuditOnly", "-CertificateDecisionForTest", "Accept", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", new string('A', 40));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("certificate", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certificateImportPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditRequiresExactPackageNameAndExactFiveChecksumArtifacts()
    {
        using var wrongName = DistributionFixture.Create();
        File.Move(
            Path.Combine(wrongName.Directory, "LumaTherm-1.0.0-win-x64.msix"),
            Path.Combine(wrongName.Directory, "LumaTherm-2.0.0-win-x64.msix"));
        DistributionFixture.RewriteChecksums(wrongName.Directory);
        var wrongPackage = wrongName.RunInstall("-AuditOnly", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", wrongName.CertificateThumbprint);
        Assert.NotEqual(0, wrongPackage.ExitCode);

        using var extraArtifact = DistributionFixture.Create();
        File.WriteAllText(Path.Combine(extraArtifact.Directory, "extra.exe"), "unexpected");
        DistributionFixture.RewriteChecksums(extraArtifact.Directory);
        var extra = extraArtifact.RunInstall("-AuditOnly", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", extraArtifact.CertificateThumbprint);
        Assert.NotEqual(0, extra.ExitCode);

        using var missingZip = DistributionFixture.Create();
        File.Delete(Path.Combine(missingZip.Directory, "LumaTherm-1.0.0-portable-win-x64.zip"));
        DistributionFixture.RewriteChecksums(missingZip.Directory);
        var missing = missingZip.RunInstall("-AuditOnly", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", missingZip.CertificateThumbprint);
        Assert.NotEqual(0, missing.ExitCode);
    }

    [Fact]
    public void AuditRejectsAnyExtraSiblingEvenWhenItIsOmittedFromChecksums()
    {
        using var fixture = DistributionFixture.Create();
        File.WriteAllText(Path.Combine(fixture.Directory, "unlisted-note.txt"), "not in SHA256SUMS.txt");

        var result = fixture.RunInstall(
            "-AuditOnly", "-SignatureStatusForTest", "Valid",
            "-SignatureThumbprintForTest", fixture.CertificateThumbprint);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packageInstallPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void UninstallAuditScopesEveryOptionalRemovalAndSupportsIdempotency()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunUninstall(
            "-AuditOnly", "-Force", "-RemoveUserData", "-RemoveCertificate",
            "-InstalledPackageForTest", "LumaTherm_1.0.0.0_x64__test",
            "-CertificateThumbprintForTest", fixture.CertificateThumbprint);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        var events = root.GetProperty("events").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Equal(new[]
        {
            "targetsPreflighted", "confirmationAccepted", "packageRemovalPlanned",
            "runValueRemovalPlanned", "userDataRemovalPlanned", "certificateRemovalPlanned",
        }, events);
        Assert.Equal("LumaTherm_1.0.0.0_x64__test", root.GetProperty("packageFullName").GetString());
        Assert.Equal("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Run", root.GetProperty("runKey").GetString());
        Assert.Equal("LumaTherm", root.GetProperty("runValue").GetString());
        Assert.EndsWith(Path.Combine("AppData", "Local", "LumaTherm"), root.GetProperty("userDataPath").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(fixture.CertificateThumbprint, root.GetProperty("certificateThumbprint").GetString());

        var emptyResult = fixture.RunUninstall("-AuditOnly", "-Force", "-InstalledPackageForTest", "");
        Assert.Equal(0, emptyResult.ExitCode);
        Assert.Contains("alreadyAbsent", emptyResult.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userDataRemovalPlanned", emptyResult.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("certificateRemovalPlanned", emptyResult.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void UninstallAuditRequiresConfirmationAndRejectsAmbiguousPackageInput()
    {
        using var fixture = DistributionFixture.Create();
        var declined = fixture.RunUninstall("-AuditOnly", "-ConfirmationDecisionForTest", "Decline", "-InstalledPackageForTest", "LumaTherm_1.0.0.0_x64__test");
        Assert.True(declined.ExitCode == 3, declined.StandardError + declined.StandardOutput);
        Assert.DoesNotContain("packageRemovalPlanned", declined.StandardOutput, StringComparison.Ordinal);

        var ambiguous = fixture.RunUninstall("-AuditOnly", "-Force", "-InstalledPackageForTest", "LumaTherm_a;LumaTherm_b");
        Assert.NotEqual(0, ambiguous.ExitCode);
        Assert.Contains("ambiguous", ambiguous.StandardError + ambiguous.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UninstallAuditRefusesAFullNameOutsideTheExactLumaThermIdentity()
    {
        using var fixture = DistributionFixture.Create();
        var result = fixture.RunUninstall("-AuditOnly", "-Force", "-InstalledPackageForTest", "OtherProduct_1.0.0.0_x64__test");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("packageRemovalPlanned", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void UninstallAuditRefusesAReparseDescendantBeforePlanningRecursiveUserDataRemoval()
    {
        using var fixture = DistributionFixture.Create();
        var localAppData = Path.Combine(fixture.Directory, "local-app-data");
        var userData = Path.Combine(localAppData, "LumaTherm");
        var external = Path.Combine(fixture.Directory, "external-user-data-sentinel");
        Directory.CreateDirectory(userData);
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "sentinel.txt");
        File.WriteAllText(sentinel, "preserve");
        var junction = Path.Combine(userData, "linked-child");
        using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", junction, external },
        })!;
        mklink.WaitForExit();
        if (mklink.ExitCode != 0)
        {
            Assert.Skip("Junction creation is unavailable on this Windows host.");
        }

        try
        {
            var result = PowerShellTestHost.Run(
                Path.Combine(fixture.Directory, "uninstall.ps1"),
                new[] { "-AuditOnly", "-Force", "-RemoveUserData", "-InstalledPackageForTest", "" },
                new Dictionary<string, string>
                {
                    ["LUMATHERM_PACKAGING_TEST"] = "1",
                    ["LOCALAPPDATA"] = localAppData,
                });

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("userDataRemovalPlanned", result.StandardOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
        }
    }

    private sealed class DistributionFixture : IDisposable
    {
        private DistributionFixture(string directory, string thumbprint)
        {
            Directory = directory;
            CertificateThumbprint = thumbprint;
        }

        public string Directory { get; }
        public string CertificateThumbprint { get; }

        public static DistributionFixture Create()
        {
            var directory = Path.Combine(RepositoryLayout.Root, "dist", "packaging-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "install.ps1"), Path.Combine(directory, "install.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "uninstall.ps1"), Path.Combine(directory, "uninstall.ps1"));
            File.WriteAllText(Path.Combine(directory, "LumaTherm-1.0.0-win-x64.msix"), "fake signed package");
            File.WriteAllText(Path.Combine(directory, "LumaTherm-1.0.0-portable-win-x64.zip"), "fake portable package");

            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LumaTherm Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(Path.Combine(directory, "LumaTherm.cer"), certificate.Export(X509ContentType.Cert));
            var thumbprint = certificate.Thumbprint;
            WriteChecksums(directory);
            return new DistributionFixture(directory, thumbprint);
        }

        public PowerShellResult RunInstall(params string[] args) => Run("install.ps1", args);
        public PowerShellResult RunUninstall(params string[] args) => Run("uninstall.ps1", args);

        private PowerShellResult Run(string script, IReadOnlyList<string> args) =>
            PowerShellTestHost.Run(Path.Combine(Directory, script), args, new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" });

        internal static void RewriteChecksums(string directory) => WriteChecksums(directory);

        private static void WriteChecksums(string directory)
        {
            var files = System.IO.Directory.GetFiles(directory).Where(path => !path.EndsWith("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
            var lines = files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .Select(path => $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))} *{Path.GetFileName(path)}");
            File.WriteAllLines(Path.Combine(directory, "SHA256SUMS.txt"), lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
