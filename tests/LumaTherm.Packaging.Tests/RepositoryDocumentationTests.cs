using System.Text.RegularExpressions;

namespace LumaTherm.Packaging.Tests;

public sealed class RepositoryDocumentationTests
{
    private static readonly string[] RequiredFiles =
    [
        "README.md",
        "README.ru.md",
        "LICENSE",
        "CHANGELOG.md",
        "SECURITY.md",
        "CONTRIBUTING.md",
        "docs/architecture.md",
        "docs/development.md",
        "docs/installation.md",
        "docs/installation.ru.md",
        "docs/portable.md",
        "docs/portable.ru.md",
        "docs/compatibility.md",
        "docs/troubleshooting.md",
        "docs/troubleshooting.ru.md",
        "docs/hardware-validation.md",
        "docs/releasing.md",
        ".github/workflows/ci.yml",
        ".github/ISSUE_TEMPLATE/bug_report.yml",
        ".github/pull_request_template.md"
    ];

    [Fact]
    public void OpenSourceReleaseDocuments_exist_and_describe_the_1_1_0_contract()
    {
        var root = RepositoryLayout.Root;
        var missing = RequiredFiles.Where(path => !File.Exists(Path.Combine(root, path))).ToArray();
        Assert.True(missing.Length == 0, $"Missing repository documentation: {string.Join(", ", missing)}");

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("1.1.0", readme, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Spark0896/LumaTherm", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("controller-gated package", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hardware checks pending", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tests remain pending", readme, StringComparison.OrdinalIgnoreCase);

        var license = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.Contains("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Spark0896", license, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(root, "docs", "compatibility.md"));
        Assert.Contains("only through the Windows LampArray API", compatibility, StringComparison.Ordinal);
        Assert.Contains("not exposed to Windows as an available LampArray cannot be controlled", compatibility, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_documentation_describes_manual_only_checks()
    {
        var root = RepositoryLayout.Root;
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var readmeRussian = File.ReadAllText(Path.Combine(root, "README.ru.md"));
        var architecture = File.ReadAllText(Path.Combine(root, "docs", "architecture.md"));

        Assert.Contains("only when you manually initiate a check", readme, StringComparison.Ordinal);
        Assert.Contains("no startup or background polling", readme, StringComparison.Ordinal);
        Assert.Contains("только когда пользователь вручную запускает проверку", readmeRussian, StringComparison.Ordinal);
        Assert.Contains("без опроса при запуске или в фоне", readmeRussian, StringComparison.Ordinal);
        Assert.Contains("only when the user manually initiates a check", architecture, StringComparison.Ordinal);
        Assert.Contains("no startup or background polling", architecture, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_documents_persisted_settings_limits_and_defaults()
    {
        var architecture = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "docs", "architecture.md"));

        Assert.Contains("three saturated default points", architecture, StringComparison.Ordinal);
        Assert.Contains("unlimited user-defined points (minimum two)", architecture, StringComparison.Ordinal);
        Assert.Contains("0–120 °C", architecture, StringComparison.Ordinal);
        Assert.Contains("at least 1 °C apart", architecture, StringComparison.Ordinal);
        Assert.Contains("0.1–5.0 seconds", architecture, StringComparison.Ordinal);
        Assert.Contains("mode and autostart disabled", architecture, StringComparison.Ordinal);
        Assert.Contains("Saving preferences does not change the live mode", architecture, StringComparison.Ordinal);
        Assert.Contains("system-language default with English fallback", architecture, StringComparison.Ordinal);
        Assert.Contains("manual English or Russian selection", architecture, StringComparison.Ordinal);
    }

    [Fact]
    public void Security_policy_uses_the_repository_private_advisory_route()
    {
        var security = File.ReadAllText(Path.Combine(RepositoryLayout.Root, "SECURITY.md"));

        Assert.Contains("https://github.com/Spark0896/LumaTherm/security/advisories/new", security, StringComparison.Ordinal);
        Assert.Contains("If GitHub private reporting is unavailable", security, StringComparison.Ordinal);
        Assert.DoesNotContain("https://github.com/Spark0896)", security, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.ru.md")]
    public void Readme_relative_links_resolve_to_repository_files(string readmeFile)
    {
        var root = RepositoryLayout.Root;
        var readme = File.ReadAllText(Path.Combine(root, readmeFile));
        var links = Regex.Matches(readme, @"(?<!!)(?:\[[^\]]+\])\((?<target>[^)#]+)(?:#[^)]+)?\)");
        var broken = links
            .Select(match => match.Groups["target"].Value.Trim())
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Where(target => !File.Exists(Path.GetFullPath(Path.Combine(root, target))))
            .ToArray();

        Assert.True(broken.Length == 0, $"{readmeFile} contains broken relative links: {string.Join(", ", broken)}");
    }
}
