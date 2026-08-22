using AiRepoKit.Cli.Models.McpDiagnostics;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.McpLaunch;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class McpPortableLaunchIntegrationTests
{
    [Fact]
    public void ResolvePortable_AndSmokeTest_UseRealPortableMcpProcess()
    {
        string repoRoot = Path.Combine(Path.GetTempPath(), "airepo-portable-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repoRoot);
        try
        {
            McpServerLaunchSpec launchSpec = McpServerLaunchSpecResolver.ResolvePortable(repoRoot);

            string targetLegacyDll = Path.Combine(repoRoot, "Tools", "AiContextMcp", "bin", "Release", "net10.0", "AiRepo.ContextMcp.dll");
            Assert.Equal(McpRuntimeKind.Portable, launchSpec.RuntimeKind);
            Assert.False(string.IsNullOrWhiteSpace(launchSpec.FileName));
            Assert.Equal(Path.GetFullPath(repoRoot), launchSpec.WorkingDirectory);
            Assert.Contains("--repo", launchSpec.Arguments, StringComparer.Ordinal);
            Assert.Contains("mcp", launchSpec.Arguments, StringComparer.Ordinal);
            Assert.Contains("serve", launchSpec.Arguments, StringComparer.Ordinal);
            Assert.False(File.Exists(targetLegacyDll));

            McpSmokeTestService smokeTestService = new();
            McpSmokeTestResult result = smokeTestService.Run(
                launchSpec,
                verbose_: false,
                strictStdio_: true,
                depth_: McpSmokeTestDepth.Minimal);

            Assert.Equal("Passed", result.Status);
            Assert.True(result.Success, result.Message);
            Assert.Equal(McpRuntimeKind.Portable, launchSpec.RuntimeKind);
            Assert.False(File.Exists(targetLegacyDll));

            string[] expectedTools =
            [
                "get_repo_brief",
                "get_health",
                "get_policy",
                "get_context",
                "search_context"
            ];

            foreach (string expectedTool in expectedTools)
            {
                Assert.Contains(expectedTool, result.ToolNames, StringComparer.Ordinal);
            }

            Assert.DoesNotContain(result.ToolNames, toolName => toolName.Contains("AiRepo.ContextMcp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(repoRoot))
            {
                try { Directory.Delete(repoRoot, true); }
                catch { }
            }
        }
    }
}
