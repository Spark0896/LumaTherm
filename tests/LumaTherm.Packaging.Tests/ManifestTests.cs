using System.Xml.Linq;
using System.Security.Cryptography;

namespace LumaTherm.Packaging.Tests;

public sealed class ManifestTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";

    [Fact]
    public void ManifestDeclaresExactFullTrustLightingAndDefaultOffStartupContracts()
    {
        var document = XDocument.Load(Path.Combine(RepositoryLayout.Root, "packaging", "AppxManifest.xml"));
        var package = Assert.IsType<XElement>(document.Root);
        var identity = Assert.Single(package.Elements(Foundation + "Identity"));
        Assert.Equal("LumaTherm", (string?)identity.Attribute("Name"));
        Assert.Equal("CN=LumaTherm Local", (string?)identity.Attribute("Publisher"));
        Assert.Equal("1.0.0.0", (string?)identity.Attribute("Version"));
        Assert.Equal("x64", (string?)identity.Attribute("ProcessorArchitecture"));

        var app = Assert.Single(package.Descendants(Foundation + "Application"));
        Assert.Equal("LumaTherm.exe", (string?)app.Attribute("Executable"));
        Assert.Equal("Windows.FullTrustApplication", (string?)app.Attribute("EntryPoint"));
        Assert.Contains(package.Descendants(Rescap + "Capability"), e => (string?)e.Attribute("Name") == "runFullTrust");

        var lighting = Assert.Single(package.Descendants(Uap3 + "Extension"), e => (string?)e.Attribute("Category") == "windows.appExtension");
        var extension = Assert.Single(lighting.Elements(Uap3 + "AppExtension"));
        Assert.Equal("com.microsoft.windows.lighting", (string?)extension.Attribute("Name"));
        Assert.Equal("public", (string?)extension.Attribute("PublicFolder"));

        var startup = Assert.Single(package.Descendants(Desktop + "StartupTask"));
        Assert.Equal("LumaThermStartup", (string?)startup.Attribute("TaskId"));
        Assert.Equal("false", (string?)startup.Attribute("Enabled"));
        Assert.DoesNotContain(package.Descendants().Where(e => e.Name.LocalName == "StartupTask"), e => string.Equals((string?)e.Attribute("Enabled"), "true", StringComparison.OrdinalIgnoreCase));

        Assert.Single(package.Elements(Foundation + "Properties"));
        Assert.Single(package.Elements(Foundation + "Dependencies"));
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
    public void ManifestReferencedAssetsAndPublicFolderExist()
    {
        var document = XDocument.Load(Path.Combine(RepositoryLayout.Root, "packaging", "AppxManifest.xml"));
        var assetValues = document.Descendants().Attributes().Select(a => a.Value)
            .Concat(document.Descendants().Select(e => e.Value))
            .Where(value => value.StartsWith("Assets\\", StringComparison.Ordinal) && value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        Assert.NotEmpty(assetValues);
        Assert.Equal(
            new[] { "Assets\\Square150x150Logo.png", "Assets\\Square44x44Logo.png", "Assets\\StoreLogo.png", "Assets\\Wide310x150Logo.png" },
            assetValues.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        foreach (var relative in assetValues)
        {
            Assert.True(File.Exists(Path.Combine(RepositoryLayout.Root, "packaging", relative)), relative);
        }

        Assert.True(File.Exists(Path.Combine(RepositoryLayout.Root, "packaging", "public", ".gitkeep")));
    }

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
