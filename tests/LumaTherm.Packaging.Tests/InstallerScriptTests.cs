using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace LumaTherm.Packaging.Tests;

public sealed class InstallerScriptTests
{
    [Fact]
    public void RegistrationAuditUsesExactExternalLocationCommandAfterAllVerification()
    {
        using var fixture = PortableFixture.Create();
        var result = fixture.Register("-AuditOnly", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", fixture.Thumbprint);

        Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        Assert.Equal(new[] { "pathsVerified", "checksumsVerified", "signatureVerified", "registrationPlanned" },
            root.GetProperty("events").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(fixture.PackagePath, root.GetProperty("identityPackage").GetString());
        Assert.Equal(fixture.ApplicationDirectory, root.GetProperty("applicationDirectory").GetString());
        Assert.Equal(new[] { "Add-AppxPackage", "-Path", fixture.PackagePath, "-ExternalLocation", fixture.ApplicationDirectory },
            root.GetProperty("registrationCommand").EnumerateArray().Select(value => value.GetString()).ToArray());
    }

    [Fact]
    public void RegistrationRejectsTamperingAndSignerCertificateMismatchBeforeMutation()
    {
        using var tampered = PortableFixture.Create();
        File.AppendAllText(tampered.PackagePath, "tampered");
        var badChecksum = tampered.Register("-AuditOnly", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", tampered.Thumbprint);
        Assert.NotEqual(0, badChecksum.ExitCode);
        Assert.DoesNotContain("registrationPlanned", badChecksum.StandardOutput, StringComparison.Ordinal);

        using var mismatched = PortableFixture.Create();
        var badSigner = mismatched.Register("-AuditOnly", "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", new string('A', 40));
        Assert.NotEqual(0, badSigner.ExitCode);
        Assert.Contains("certificate", badSigner.StandardError + badSigner.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registrationPlanned", badSigner.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void UntrustedSignatureRequiresAdministratorAndExplicitConfirmationBeforeTrustPlan()
    {
        using var fixture = PortableFixture.Create();
        var nonAdmin = fixture.Register("-AuditOnly", "-SignatureStatusForTest", "NotTrusted", "-SignatureThumbprintForTest", fixture.Thumbprint,
            "-AdministratorStatusForTest", "NonAdmin", "-CertificateDecisionForTest", "Accept");
        Assert.NotEqual(0, nonAdmin.ExitCode);
        Assert.DoesNotContain("certificateImportPlanned", nonAdmin.StandardOutput, StringComparison.Ordinal);

        var declined = fixture.Register("-AuditOnly", "-SignatureStatusForTest", "NotTrusted", "-SignatureThumbprintForTest", fixture.Thumbprint,
            "-AdministratorStatusForTest", "Admin", "-CertificateDecisionForTest", "Decline");
        Assert.Equal(3, declined.ExitCode);
        Assert.DoesNotContain("certificateImportPlanned", declined.StandardOutput, StringComparison.Ordinal);

        var accepted = fixture.Register("-AuditOnly", "-SignatureStatusForTest", "NotTrusted", "-SignatureThumbprintForTest", fixture.Thumbprint,
            "-AdministratorStatusForTest", "Admin", "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid");
        Assert.Equal(0, accepted.ExitCode);
        using var json = JsonDocument.Parse(accepted.StandardOutput);
        var events = json.RootElement.GetProperty("events").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.True(Array.IndexOf(events, "certificateImportConfirmed") < Array.IndexOf(events, "certificateImportPlanned"));
        Assert.True(Array.IndexOf(events, "signatureReverified") < Array.IndexOf(events, "registrationPlanned"));
    }

    [Fact]
    public void RegistrationAcceptsUnknownErrorOnlyForTheExactUntrustedRootCase()
    {
        using var fixture = PortableFixture.Create();
        var accepted = fixture.Register("-AuditOnly", "-SignatureStatusForTest", "UnknownError", "-SignatureTrustIssueForTest", "UntrustedRoot",
            "-SignatureThumbprintForTest", fixture.Thumbprint, "-AdministratorStatusForTest", "Admin", "-CertificateDecisionForTest", "Accept", "-ReverifiedSignatureStatusForTest", "Valid");
        Assert.Equal(0, accepted.ExitCode);
        Assert.Contains("registrationPlanned", accepted.StandardOutput, StringComparison.Ordinal);

        var rejected = fixture.Register("-AuditOnly", "-SignatureStatusForTest", "UnknownError", "-SignatureTrustIssueForTest", "Other",
            "-SignatureThumbprintForTest", fixture.Thumbprint, "-AdministratorStatusForTest", "Admin", "-CertificateDecisionForTest", "Accept");
        Assert.NotEqual(0, rejected.ExitCode);
        Assert.DoesNotContain("certificateImportPlanned", rejected.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("registrationPlanned", rejected.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistrationRejectsTraversalAndReparseEscapeBeforeMutationPlan()
    {
        using var fixture = PortableFixture.Create();
        var external = Path.Combine(Path.GetDirectoryName(fixture.Root)!, "outside.msix");
        File.WriteAllText(external, "outside");
        try
        {
            var traversal = fixture.Register("-AuditOnly", "-IdentityPackage", external, "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", fixture.Thumbprint);
            Assert.NotEqual(0, traversal.ExitCode);
            Assert.Contains("outside", traversal.StandardError + traversal.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("registrationPlanned", traversal.StandardOutput, StringComparison.Ordinal);

            var link = Path.Combine(fixture.Root, "linked-app");
            if (!TryCreateJunction(link, fixture.ApplicationDirectory)) return;
            try
            {
                var reparse = fixture.Register("-AuditOnly", "-ApplicationDirectory", link, "-SignatureStatusForTest", "Valid", "-SignatureThumbprintForTest", fixture.Thumbprint);
                Assert.NotEqual(0, reparse.ExitCode);
                Assert.Contains("reparse", reparse.StandardError + reparse.StandardOutput, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("registrationPlanned", reparse.StandardOutput, StringComparison.Ordinal);
            }
            finally { Directory.Delete(link); }
        }
        finally { File.Delete(external); }
    }

    [Fact]
    public void UnregistrationTargetsOnlyExactNameAndPublisherWithoutWildcards()
    {
        using var fixture = PortableFixture.Create();
        var exact = fixture.Unregister("-AuditOnly", "-Force", "-InstalledPackageNameForTest", "LumaTherm",
            "-InstalledPublisherForTest", "CN=LumaTherm Local", "-InstalledPackageFullNameForTest", "LumaTherm_1.1.0.0_x64__test");
        Assert.Equal(0, exact.ExitCode);
        using var json = JsonDocument.Parse(exact.StandardOutput);
        Assert.Equal(new[] { "Remove-AppxPackage", "-Package", "LumaTherm_1.1.0.0_x64__test" },
            json.RootElement.GetProperty("removalCommand").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(json.RootElement.GetProperty("removeUserData").GetBoolean());

        var lookalike = fixture.Unregister("-AuditOnly", "-Force", "-InstalledPackageNameForTest", "LumaTherm.Helper",
            "-InstalledPublisherForTest", "CN=LumaTherm Local", "-InstalledPackageFullNameForTest", "LumaTherm.Helper_1.0_x64__test");
        Assert.NotEqual(0, lookalike.ExitCode);
        Assert.DoesNotContain("packageRemovalPlanned", lookalike.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void UserDataCleanupRejectsAReparseDescendantBeforePlanningDeletion()
    {
        using var fixture = PortableFixture.Create();
        var localRoot = Path.Combine(fixture.Root, "local-app-data");
        var userData = Path.Combine(localRoot, "LumaTherm");
        Directory.CreateDirectory(userData);
        var external = Path.Combine(RepositoryLayout.Root, "artifacts", "external-user-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(external);
        var sentinel = Path.Combine(external, "sentinel.txt");
        File.WriteAllText(sentinel, "preserve");
        var junction = Path.Combine(userData, "linked");
        if (!TryCreateJunction(junction, external))
        {
            Directory.Delete(external, recursive: true);
            Assert.Skip("Junction creation is unavailable on this Windows host.");
        }

        try
        {
            var result = fixture.UnregisterWithLocalAppData(localRoot, "-AuditOnly", "-Force", "-RemoveUserData");
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("userDataRemovalPlanned", result.StandardOutput, StringComparison.Ordinal);
            Assert.True(File.Exists(sentinel));
        }
        finally
        {
            if (Directory.Exists(junction)) Directory.Delete(junction);
            if (Directory.Exists(external)) Directory.Delete(external, recursive: true);
        }
    }

    [Fact]
    public void InnoDefinitionHasStableUpgradeIdentityAndRequestedShortcutDefaults()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryLayout.Root, "packaging", "LumaTherm.iss"));
        var values = ParseKeyValues(lines);
        Assert.Equal("{{9F6F5FEA-A89E-4D1C-9D0C-6C7C9FB5D310}", values["AppId"]);
        Assert.Equal("LumaTherm", values["AppName"]);
        Assert.Equal("1.1.0", values["AppVersion"]);
        Assert.Equal(@"{autopf}\LumaTherm", values["DefaultDirName"]);
        Assert.Equal("x64compatible", values["ArchitecturesAllowed"]);
        Assert.Equal("admin", values["PrivilegesRequired"]);
        Assert.Contains(lines, line => line.Contains("Name: \"desktopicon\"") && line.Contains("Flags: unchecked"));
        Assert.Contains(lines, line => line.Contains("Name: \"startmenuicon\"") && line.Contains("Flags: checkedonce"));
        Assert.Contains(lines, line => line.Contains("CloseApplicationsFilter=LumaTherm.exe", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Register-LumaTherm.ps1", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Unregister-LumaTherm.ps1", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("settings.json", StringComparison.OrdinalIgnoreCase) && line.Contains("[UninstallDelete]", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("DestDir: \"{app}\\payload\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("{app}\\payload\\app\\LumaTherm.exe", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("-PortableDirectory \"\"{app}\\payload\"\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Registration failed", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("RaiseException", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("DestDir: \"{app}\\app\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Type: filesandordirs; Name: \"{app}\\payload\"", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("not UninstallSilent", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("-RemoveUserData", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("DelTree", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ParseKeyValues(string[] lines) => lines
        .Where(line => !line.StartsWith(';') && line.Contains('='))
        .Select(line => line.Split('=', 2))
        .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool TryCreateJunction(string junction, string target)
    {
        var process = BoundedProcessTestHost.Run(new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/c", "mklink", "/J", junction, target },
        }, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        return process.ExitCode == 0;
    }

    private sealed class PortableFixture : IDisposable
    {
        private PortableFixture(string root, string thumbprint)
        {
            Root = root;
            Thumbprint = thumbprint;
        }

        public string Root { get; }
        public string Thumbprint { get; }
        public string PackagePath => Path.Combine(Root, "LumaTherm-1.1.0-sparse.msix");
        public string ApplicationDirectory => Path.Combine(Root, "app");

        public static PortableFixture Create()
        {
            var root = Path.Combine(RepositoryLayout.Root, "artifacts", "packaging-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "app"));
            File.WriteAllText(Path.Combine(root, "app", "LumaTherm.exe"), "self-contained app");
            File.WriteAllText(Path.Combine(root, "LumaTherm-1.1.0-sparse.msix"), "signed sparse identity");
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Register-LumaTherm.ps1"), Path.Combine(root, "Register-LumaTherm.ps1"));
            File.Copy(Path.Combine(RepositoryLayout.Root, "scripts", "Unregister-LumaTherm.ps1"), Path.Combine(root, "Unregister-LumaTherm.ps1"));

            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=LumaTherm Local", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(Path.Combine(root, "LumaTherm.cer"), certificate.Export(X509ContentType.Cert));
            WriteChecksums(root);
            return new PortableFixture(root, certificate.Thumbprint);
        }

        public PowerShellResult Register(params string[] arguments) => Run("Register-LumaTherm.ps1", arguments);
        public PowerShellResult Unregister(params string[] arguments) => Run("Unregister-LumaTherm.ps1", arguments);
        public PowerShellResult UnregisterWithLocalAppData(string localAppData, params string[] arguments) => PowerShellTestHost.Run(
            Path.Combine(Root, "Unregister-LumaTherm.ps1"), arguments,
            new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1", ["LOCALAPPDATA"] = localAppData });

        private PowerShellResult Run(string script, IReadOnlyList<string> arguments) => PowerShellTestHost.Run(
            Path.Combine(Root, script), arguments, new Dictionary<string, string> { ["LUMATHERM_PACKAGING_TEST"] = "1" });

        private static void WriteChecksums(string root)
        {
            var names = new[] { "app/LumaTherm.exe", "LumaTherm-1.1.0-sparse.msix", "LumaTherm.cer", "Register-LumaTherm.ps1", "Unregister-LumaTherm.ps1" };
            var lines = names.Select(name => $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)))))} *{name}");
            File.WriteAllLines(Path.Combine(root, "SHA256SUMS.txt"), lines, new UTF8Encoding(false));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
