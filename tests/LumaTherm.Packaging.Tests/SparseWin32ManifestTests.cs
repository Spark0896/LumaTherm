using System.Xml.Linq;

namespace LumaTherm.Packaging.Tests;

public sealed class SparseWin32ManifestTests
{
    [Fact]
    public void ExternalLocationApplicationUsesTheWin32MediumIlContractWithoutEntrypoint()
    {
        XNamespace foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
        XNamespace uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
        var manifest = XDocument.Load(Path.Combine(
            RepositoryLayout.Root, "packaging", "sparse", "AppxManifest.xml"));
        var application = Assert.Single(manifest.Descendants(foundation + "Application"));

        Assert.Equal("win32App", (string?)application.Attribute(uap10 + "RuntimeBehavior"));
        Assert.Equal("mediumIL", (string?)application.Attribute(uap10 + "TrustLevel"));
        Assert.Null(application.Attribute("EntryPoint"));
    }
}
