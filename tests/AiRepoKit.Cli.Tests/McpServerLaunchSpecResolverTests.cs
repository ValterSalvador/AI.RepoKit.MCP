using System.Diagnostics;
using AiRepoKit.Cli.Services.McpBudget;
using AiRepoKit.Cli.Services.McpLaunch;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class McpServerLaunchSpecResolverTests
{
    [Fact]
    public void ResolvePortable_UsesPortableRuntimeAndNormalizedRepoPath()
    {
        string repoRoot = Path.Combine(Path.GetTempPath(), "airepo-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repoRoot);
        try
        {
            string repoInput = Path.Combine(repoRoot, ".");
            string targetDll = Path.Combine(repoRoot, "Tools", "AiContextMcp", "bin", "Release", "net10.0", "AiRepo.ContextMcp.dll");

            McpServerLaunchSpec launchSpec = McpServerLaunchSpecResolver.ResolvePortable(repoInput);

            Assert.Equal(McpRuntimeKind.Portable, launchSpec.RuntimeKind);
            Assert.False(string.IsNullOrWhiteSpace(launchSpec.FileName));
            Assert.Equal(Path.GetFullPath(repoInput), launchSpec.WorkingDirectory);
            Assert.InRange(launchSpec.Arguments.Count, 4, 5);
            int repoArgIndex = launchSpec.Arguments.Count - 1;
            Assert.Equal(Path.GetFullPath(repoInput), launchSpec.Arguments[repoArgIndex]);
            if (repoArgIndex == 4)
            {
                Assert.EndsWith(".dll", launchSpec.Arguments[0], StringComparison.OrdinalIgnoreCase);
                Assert.Contains("AiRepoKit.Cli", launchSpec.Arguments[0], StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal("mcp", launchSpec.Arguments[repoArgIndex - 3]);
            Assert.Equal("serve", launchSpec.Arguments[repoArgIndex - 2]);
            Assert.Equal("--repo", launchSpec.Arguments[repoArgIndex - 1]);
            Assert.DoesNotContain(launchSpec.Arguments, arg => arg.Contains("AiRepo.ContextMcp.dll", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(targetDll, string.Join(' ', launchSpec.Arguments), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(repoRoot, true);
        }
    }

    [Fact]
    public void ResolveLegacy_UsesDotnetAndTargetDll()
    {
        string repoRoot = Path.Combine(Path.GetTempPath(), "airepo-legacy-launch-" + Guid.NewGuid().ToString("N"));
        string dllPath = Path.Combine(repoRoot, "Tools", "AiContextMcp", "bin", "Release", "net10.0", "AiRepo.ContextMcp.dll");

        McpServerLaunchSpec launchSpec = McpServerLaunchSpecResolver.ResolveLegacy(repoRoot, dllPath);

        Assert.Equal(McpRuntimeKind.Legacy, launchSpec.RuntimeKind);
        Assert.Equal("dotnet", launchSpec.FileName);
        Assert.Equal(Path.GetFullPath(repoRoot), launchSpec.WorkingDirectory);
        Assert.Equal(3, launchSpec.Arguments.Count);
        Assert.Equal(Path.GetFullPath(dllPath), launchSpec.Arguments[0]);
        Assert.Equal("--repo", launchSpec.Arguments[1]);
        Assert.Equal(Path.GetFullPath(repoRoot), launchSpec.Arguments[2]);
    }

    [Fact]
    public void CreateProcessStartInfo_UsesLaunchSpecValues()
    {
        McpServerLaunchSpec launchSpec = new(
            "dotnet",
            ["AiRepoKit.Cli.dll", "mcp", "serve", "--repo", @"C:\repo"],
            @"C:\repo",
            McpRuntimeKind.Portable);

        ProcessStartInfo processStartInfo = McpStdioSession.CreateProcessStartInfo(launchSpec);

        Assert.Equal(launchSpec.FileName, processStartInfo.FileName);
        Assert.Equal(launchSpec.WorkingDirectory, processStartInfo.WorkingDirectory);
        Assert.False(processStartInfo.UseShellExecute);
        Assert.True(processStartInfo.RedirectStandardInput);
        Assert.True(processStartInfo.RedirectStandardOutput);
        Assert.True(processStartInfo.RedirectStandardError);
        Assert.True(processStartInfo.CreateNoWindow);
        Assert.Equal(launchSpec.Arguments, processStartInfo.ArgumentList.ToArray());
    }
}
