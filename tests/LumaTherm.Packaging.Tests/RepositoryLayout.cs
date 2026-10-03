namespace LumaTherm.Packaging.Tests;

internal static class RepositoryLayout
{
    public static string Root { get; } = FindRoot();

    public static string DotnetHost
    {
        get
        {
            var local = Path.Combine(Root, ".dotnet", "dotnet.exe");
            if (File.Exists(local)) return local;
            var inherited = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            return !string.IsNullOrWhiteSpace(inherited) && File.Exists(inherited) ? inherited : "dotnet";
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LumaTherm.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
