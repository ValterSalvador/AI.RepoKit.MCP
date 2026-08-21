using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.SdkAlignment;

namespace AiRepoKit.Cli.Commands;

public sealed class SdkAlignmentCommand
{
    private readonly ISdkAlignmentService _sdkAlignmentService;

    public SdkAlignmentCommand()
        : this(new SdkAlignmentService())
    {
    }

    internal SdkAlignmentCommand(
        ISdkAlignmentService sdkAlignmentService_)
    {
        this._sdkAlignmentService =
            sdkAlignmentService_ ??
            throw new ArgumentNullException(
                nameof(sdkAlignmentService_));
    }

    public CommandResult Execute(
        BootstrapOptions options_)
    {
        string repoRoot =
            Path.GetFullPath(
                options_.RepoPath);

        SdkAlignmentRunResult result;

        try
        {
            result =
                this._sdkAlignmentService.Run(
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

        int projectCount =
            result.Report?.Projects.Count ??
            0;

        string markdown =
            string.Join(
                Environment.NewLine,
                [
                    "# SDK Alignment",
                    "",
                    $"- Repo: `{repoRoot}`",
                    "- Status: `Passed`",
                    $"- Projects: `{projectCount}`",
                    "- Report: `.ai/generated/reports/sdk-alignment-report.json`"
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
                "# SDK Alignment",
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
