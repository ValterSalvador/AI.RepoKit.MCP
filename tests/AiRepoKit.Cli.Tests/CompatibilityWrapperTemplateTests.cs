using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class CompatibilityWrapperTemplateTests
{
    private static readonly string[] WrapperNames =
    [
        "UpdateAiContext",
        "CheckSdkAlignment",
        "UpdateCodeInventory",
        "InvokeBuildDiagnostics",
        "CheckSecrets",
        "MeasureMcpResponseBudget"
    ];

    [Fact]
    public void Templates_ContainPowerShellAndBashWrappers()
    {
        IReadOnlyList<string> templates =
            new TemplateService()
                .ListTemplates();

        foreach (string name in WrapperNames)
        {
            Assert.Contains(
                $"tools-ai-context/{name}.ps1.tpl",
                templates);

            Assert.Contains(
                $"tools-ai-context/{name}.sh.tpl",
                templates);
        }
    }

    [Fact]
    public void WrapperTemplates_ContainDelegationWithoutProductBusinessLogic()
    {
        string root =
            new TemplateService()
                .GetTemplateRoot();

        string[] forbidden =
        [
            "Get-ChildItem",
            "ConvertTo-Json",
            "Get-CSharpFiles",
            "ProcessStartInfo",
            "JsonSerializer",
            "Regex("
        ];

        foreach (string name in WrapperNames)
        {
            foreach (string extension in
                new[] { "ps1", "sh" })
            {
                string path =
                    Path.Combine(
                        root,
                        "tools-ai-context",
                        $"{name}.{extension}.tpl");

                string content =
                    File.ReadAllText(
                        path);

                Assert.Contains(
                    "airepo",
                    content);

                foreach (string value in forbidden)
                {
                    Assert.False(
                        content.Contains(
                            value,
                            StringComparison.OrdinalIgnoreCase),
                        $"{path} contains historical business logic marker {value}.");
                }
            }
        }
    }

    [Fact]
    public void ClientConfigTemplates_UsePortableMcpLaunchShape()
    {
        string root =
            new TemplateService()
                .GetTemplateRoot();

        string[] clientConfigTemplates =
        [
            "client-configs/codex.config.toml.tpl",
            "client-configs/codex.config.snippet.toml.tpl",
            "client-configs/visualstudio-mcp.snippet.json.tpl",
            "client-configs/visualstudio.mcp.json.tpl",
            "client-configs/visualstudio.local.mcp.json.tpl",
            "client-configs/vscode.mcp.json.tpl",
            "client-configs/claude_desktop_config.snippet.json.tpl",
            "client-configs/cursor-mcp.snippet.json.tpl",
            "client-configs/gemini-mcp.snippet.json.tpl"
        ];

        string[] forbidden =
        [
            "AiRepo.ContextMcp.dll",
            "Tools/AiContextMcp/bin"
        ];

        foreach (string relativePath in clientConfigTemplates)
        {
            string path =
                Path.Combine(
                    root,
                    relativePath);

            string content =
                File.ReadAllText(path);

            Assert.Contains("{{ToolCommandName}}", content);
            Assert.Contains("mcp", content);
            Assert.Contains("serve", content);
            Assert.Contains("--repo", content);

            foreach (string value in forbidden)
            {
                Assert.DoesNotContain(value, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void McpBudgetScriptDefinition_ProvidesBothShellWrappers()
    {
        Assert.Equal(
            "Tools/AiContext/MeasureMcpResponseBudget.ps1",
            ScriptDefinition.McpBudget.PowerShellRelativePath);

        Assert.Equal(
            "Tools/AiContext/MeasureMcpResponseBudget.sh",
            ScriptDefinition.McpBudget.BashRelativePath);
    }
}
