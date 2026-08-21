using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.SecretScan;

namespace AiRepoKit.Cli.Commands;

public sealed class SecretScanCommand
{
    private readonly ISecretScanService _secretScanService;

    public SecretScanCommand()
        : this(new SecretScanService())
    {
    }

    internal SecretScanCommand(
        ISecretScanService secretScanService_)
    {
        this._secretScanService =
            secretScanService_ ??
            throw new ArgumentNullException(
                nameof(secretScanService_));
    }

    public CommandResult Execute(
        BootstrapOptions options_)
    {
        string repoRoot =
            Path.GetFullPath(
                options_.RepoPath);

        SecretScanRunResult result;

        try
        {
            result =
                this._secretScanService.Run(
                    repoRoot);
        }
        catch (Exception exception)
        {
            return CommandResult.Failure(
                WriteFailure(
                    repoRoot,
                    exception.Message),
                1);
        }

        if (!result.IsSuccess)
        {
            return CommandResult.Failure(
                WriteFailure(
                    repoRoot,
                    result.ErrorMessage ??
                    "Unknown failure."),
                1);
        }

        int findingCount =
            result.Report?.FindingCount ??
            0;

        string markdown =
            string.Join(
                Environment.NewLine,
                [
                    "# Secret Scan",
                    "",
                    $"- Repo: `{repoRoot}`",
                    "- Status: `Passed`",
                    $"- Findings: `{findingCount}`",
                    "- Report: `.ai/generated/reports/secret-scan-report.json`",
                    "- Secret values returned: `False`"
                ]);

        return CommandResult.Ok(
            markdown);
    }

    private static string WriteFailure(
        string repoRoot_,
        string message_)
    {
        return string.Join(
            Environment.NewLine,
            [
                "# Secret Scan",
                "",
                $"- Repo: `{repoRoot_}`",
                "- Status: `Failed`",
                "",
                "## Error",
                "",
                $"- {ProcessRunner.Redact(message_)}"
            ]);
    }
}
