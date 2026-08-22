using System.Reflection;

namespace AiRepoKit.Cli.Services.McpLaunch;

internal static class McpServerLaunchSpecResolver
{
    internal static McpServerLaunchSpec ResolvePortable(string repoPath_)
    {
        string repoPath = Path.GetFullPath(repoPath_);

        if (IsCurrentProcessCliAppHost())
        {
            return new McpServerLaunchSpec(
                Environment.ProcessPath!,
                ["mcp", "serve", "--repo", repoPath],
                repoPath,
                McpRuntimeKind.Portable);
        }

        string cliAssemblyPath = GetCliAssemblyPath();
        return new McpServerLaunchSpec(
            GetDotnetExecutable(),
            [cliAssemblyPath, "mcp", "serve", "--repo", repoPath],
            repoPath,
            McpRuntimeKind.Portable);
    }

    internal static McpServerLaunchSpec ResolveLegacy(string repoPath_, string dllPath_)
    {
        string repoPath = Path.GetFullPath(repoPath_);
        string dllPath = Path.GetFullPath(dllPath_);

        return new McpServerLaunchSpec(
            "dotnet",
            [dllPath, "--repo", repoPath],
            repoPath,
            McpRuntimeKind.Legacy);
    }

    private static bool IsCurrentProcessCliAppHost()
    {
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        if (!ReferenceEquals(entryAssembly, typeof(McpServerLaunchSpecResolver).Assembly))
        {
            return false;
        }

        string? processPath = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(processPath) && !IsDotnetHostExecutable(processPath);
    }

    private static string GetDotnetExecutable()
    {
        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && IsDotnetHostExecutable(processPath))
        {
            return Path.GetFullPath(processPath);
        }

        return "dotnet";
    }

    private static bool IsDotnetHostExecutable(string processPath) =>
        string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);

    private static string GetCliAssemblyPath()
    {
        string location = typeof(McpServerLaunchSpecResolver).Assembly.Location;
        if (string.IsNullOrWhiteSpace(location))
        {
            string assemblyName = typeof(McpServerLaunchSpecResolver).Assembly.GetName().Name ?? "AiRepoKit.Cli";
            location = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        }

        return Path.GetFullPath(location);
    }
}
