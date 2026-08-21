using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.McpBudget;

namespace AiRepoKit.Cli.Commands;

public sealed class McpBudgetCommand
{
    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private readonly IMcpBudgetService _mcpBudgetService;

    public McpBudgetCommand()
        : this(new McpBudgetService())
    {
    }

    internal McpBudgetCommand(
        IMcpBudgetService mcpBudgetService_)
    {
        this._mcpBudgetService =
            mcpBudgetService_ ??
            throw new ArgumentNullException(
                nameof(mcpBudgetService_));
    }

    public CommandResult Execute(
        BootstrapOptions options_)
    {
        string repoRoot =
            Path.GetFullPath(
                options_.RepoPath);

        McpBudgetRunResult result;

        try
        {
            result =
                this._mcpBudgetService.Run(
                    repoRoot,
                    new McpBudgetOptions(
                        options_.StartupTimeoutSeconds,
                        options_.ToolTimeoutSeconds));
        }
        catch (Exception exception)
        {
            string failure =
                string.Join(
                    Environment.NewLine,
                    [
                        "# MCP Budget",
                        "",
                        $"- Repo: `{repoRoot}`",
                        "- Status: `Failed`",
                        "",
                        "## Error",
                        "",
                        $"- {ProcessRunner.Redact(exception.Message)}"
                    ]);

            return CommandResult.Failure(
                failure,
                1);
        }

        string output =
            options_.AuditJson
                ? JsonSerializer.Serialize(
                    result.Report,
                    JsonOptions)
                : WriteMarkdown(
                    repoRoot,
                    result);

        return new CommandResult(
            result.IsSuccess,
            output,
            (int)result.ExitClass);
    }

    private static string WriteMarkdown(
        string repoRoot_,
        McpBudgetRunResult result_)
    {
        List<string> lines =
        [
            "# MCP Budget",
            "",
            $"- Repo: `{repoRoot_}`",
            $"- Status: `{(result_.IsSuccess ? "Passed" : "Failed")}`",
            $"- Exit class: `{(int)result_.ExitClass}`",
            $"- Calls measured: `{result_.Report.Results.Count}`",
            $"- Failures: `{result_.Report.Failures.Count}`",
            "- JSON report: `.ai/generated/reports/mcp-budget-report.json`",
            "- Markdown report: `.ai/generated/reports/mcp-budget-report.md`"
        ];

        if (result_.Report.Failures.Count > 0)
        {
            lines.Add("");
            lines.Add("## Failures");
            lines.Add("");

            foreach (string failure in
                result_.Report.Failures.Take(5))
            {
                lines.Add(
                    $"- {ProcessRunner.Redact(failure)}");
            }
        }

        return string.Join(
            Environment.NewLine,
            lines);
    }
}
