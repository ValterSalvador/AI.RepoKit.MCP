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
