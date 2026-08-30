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

        var readmePath = Path.Combine(root, "README.md");
        var readme = File.ReadAllText(readmePath);
        Assert.Contains("1.1.0", readme, StringComparison.Ordinal);
        Assert.Contains("https://github.com/Spark0896/LumaTherm", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("controller-gated package", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hardware checks pending", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tests remain pending", readme, StringComparison.OrdinalIgnoreCase);

        var license = File.ReadAllText(Path.Combine(root, "LICENSE"));
        Assert.Contains("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Spark0896", license, StringComparison.Ordinal);

        var compatibility = File.ReadAllText(Path.Combine(root, "docs", "compatibility.md"));
        Assert.DoesNotContain("raw HID", compatibility, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vendor DLL", compatibility, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_relative_links_resolve_to_repository_files()
    {
        var root = RepositoryLayout.Root;
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var links = Regex.Matches(readme, @"(?<!!)(?:\[[^\]]+\])\((?<target>[^)#]+)(?:#[^)]+)?\)");
        var broken = links
            .Select(match => match.Groups["target"].Value.Trim())
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            .Where(target => !File.Exists(Path.GetFullPath(Path.Combine(root, target))))
            .ToArray();

        Assert.True(broken.Length == 0, $"README contains broken relative links: {string.Join(", ", broken)}");
    }
}
