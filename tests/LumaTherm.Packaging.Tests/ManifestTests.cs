using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace LumaTherm.Packaging.Tests;

public sealed class ManifestTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Msix = "urn:schemas-microsoft-com:msix.v1";

    [Fact]
    public void NativeAndSparseManifestsDeclareTheSameExactIdentity()
    {
        var sparsePath = Path.Combine(RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml");
        Assert.True(File.Exists(sparsePath));
        Assert.False(File.Exists(Path.Combine(RepositoryLayout.Root, "packaging", "AppxManifest.xml")));

        var sparse = XDocument.Load(sparsePath);
        var identity = Assert.Single(sparse.Root!.Elements(Foundation + "Identity"));
        Assert.Equal("LumaTherm", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=LumaTherm Local", (string?)identity.Attribute("Publisher"));
        Assert.Equal("1.1.0.0", (string?)identity.Attribute("Version"));
        Assert.Equal("x64", (string?)identity.Attribute("ProcessorArchitecture"));

        var application = Assert.Single(sparse.Descendants(Foundation + "Application"));
        Assert.Equal("LumaTherm", (string?)application.Attribute("Id"));

        var native = XDocument.Load(Path.Combine(RepositoryLayout.Root, "src", "LumaTherm.App", "app.manifest"));
        var nativeIdentity = Assert.Single(native.Descendants(Msix + "identity"));
        Assert.Equal((string?)identity.Attribute("Name"), (string?)nativeIdentity.Attribute("packageName"));
        Assert.Equal((string?)identity.Attribute("Publisher"), (string?)nativeIdentity.Attribute("publisher"));
        Assert.Equal((string?)application.Attribute("Id"), (string?)nativeIdentity.Attribute("applicationId"));
    }

    [Fact]
    public void SparseManifestUsesExternalLocationAndOnlyTheLightingExtension()
    {
        var document = XDocument.Load(Path.Combine(RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml"));
        var package = document.Root!;
        Assert.Equal("true", Assert.Single(package.Descendants(Uap10 + "AllowExternalContent")).Value);

        var app = Assert.Single(package.Descendants(Foundation + "Application"));
        Assert.Equal("LumaTherm.exe", (string?)app.Attribute("Executable"));
        Assert.Equal("windowsApp", (string?)app.Attribute(Uap10 + "RuntimeBehavior"));
        Assert.Equal("mediumIL", (string?)app.Attribute(Uap10 + "TrustLevel"));

        var lighting = Assert.Single(package.Descendants(Uap3 + "Extension"));
        Assert.Equal("windows.appExtension", (string?)lighting.Attribute("Category"));
        var extension = Assert.Single(lighting.Elements(Uap3 + "AppExtension"));
        Assert.Equal("com.microsoft.windows.lighting", (string?)extension.Attribute("Name"));
        Assert.Equal("public", (string?)extension.Attribute("PublicFolder"));
        Assert.DoesNotContain(package.Descendants(), node => node.Name.LocalName is "StartupTask" or "Capability");
        Assert.Single(app.Elements(Uap + "VisualElements"));
    }

    [Theory]
    [InlineData("StoreLogo.png", 50, 50, "243830D4498910D6740B8567B6B9BB4692E09F0423CB8EBB8473F9204D10B940")]
    [InlineData("Square44x44Logo.png", 44, 44, "952FBFD4BE7E566E23C4AC02D999F6DFEA0FE3CF1F08F62BD83EC6C06B00C7E2")]
    [InlineData("Square150x150Logo.png", 150, 150, "AC3C076773D321B12B1292654EFA3762A64104D326F23D32471269D73F2C3F56")]
    [InlineData("Wide310x150Logo.png", 310, 150, "0DB9C40921D50CA1A08DCBD51C4593010A4830F893BBA0AFCBE50C1D2B47922C")]
    public void ApprovedPngHasExactDimensionsAndHash(string fileName, int expectedWidth, int expectedHeight, string expectedHash)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryLayout.Root, "packaging", "Assets", fileName));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(expectedWidth, ReadBigEndianInt32(bytes, 16));
        Assert.Equal(expectedHeight, ReadBigEndianInt32(bytes, 20));
        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    [Fact]
    public void AssetBuilderReproducesEveryManifestReferencedPngByteForByte()
    {
        var manifest = XDocument.Load(Path.Combine(RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml"));
        var referencedAssets = manifest.Descendants()
            .SelectMany(element => element.Attributes().Select(attribute => attribute.Value)
                .Concat(element.HasElements ? Array.Empty<string>() : new[] { element.Value.Trim() }))
            .Where(value => value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Select(value => Path.GetFileName(value)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[]
        {
            "Square150x150Logo.png",
            "Square44x44Logo.png",
            "StoreLogo.png",
            "Wide310x150Logo.png"
        }, referencedAssets);

        var generatedRoot = Path.Combine(Path.GetTempPath(), "LumaTherm-asset-builder-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var generatedSource = Path.Combine(generatedRoot, "src", "LumaTherm.App", "Assets");
            Directory.CreateDirectory(generatedSource);
            File.Copy(Path.Combine(RepositoryLayout.Root, "src", "LumaTherm.App", "Assets", "LogoGeometry.xaml"),
                Path.Combine(generatedSource, "LogoGeometry.xaml"));
            var startInfo = new ProcessStartInfo(Path.Combine(RepositoryLayout.Root, ".dotnet", "dotnet.exe"))
            {
                WorkingDirectory = RepositoryLayout.Root
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(Path.Combine(RepositoryLayout.Root, "tools", "LumaTherm.AssetBuilder", "LumaTherm.AssetBuilder.csproj"));
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("Release");
            startInfo.ArgumentList.Add("-p:NuGetAudit=false");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(generatedRoot);
            var result = BoundedProcessTestHost.Run(startInfo, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
            Assert.True(result.ExitCode == 0, result.StandardError + result.StandardOutput);

            var generatedAssets = Path.Combine(generatedRoot, "packaging", "Assets");
            Assert.Equal(referencedAssets, Directory.GetFiles(generatedAssets, "*.png")
                .Select(path => Path.GetFileName(path)!).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            foreach (var fileName in referencedAssets)
            {
                var committed = Path.Combine(RepositoryLayout.Root, "packaging", "Assets", fileName);
                var generated = Path.Combine(generatedAssets, fileName);
                Assert.True(File.Exists(committed), $"Manifest-referenced asset is missing: {fileName}");
                Assert.Equal(File.ReadAllBytes(committed), File.ReadAllBytes(generated));
            }
        }
        finally { if (Directory.Exists(generatedRoot)) Directory.Delete(generatedRoot, recursive: true); }
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
