namespace PTenoLauncher.Tests;

internal static class TestDotnetHost
{
    public static string Resolve(string projectRoot)
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var runningHost = Environment.ProcessPath;
        if (runningHost is not null && Path.GetFileName(runningHost).Equals(name, StringComparison.OrdinalIgnoreCase))
            return runningHost;

        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"),
            Path.Combine(projectRoot, ".tools", "dotnet", name),
            string.IsNullOrEmpty(root) ? null : Path.Combine(root, name)
        };
        foreach (var candidate in candidates)
            if (candidate is not null && Path.IsPathFullyQualified(candidate) && File.Exists(candidate))
                return candidate;

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (Path.IsPathFullyQualified(directory) && File.Exists(Path.Combine(directory, name)))
                return Path.Combine(directory, name);

        throw new FileNotFoundException("Could not locate the .NET host for the child test process.");
    }
}
