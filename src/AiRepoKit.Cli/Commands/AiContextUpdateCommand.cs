using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.AiContextUpdate;

namespace AiRepoKit.Cli.Commands;

public sealed class AiContextUpdateCommand
{
    private readonly IAiContextUpdateService _aiContextUpdateService;

    public AiContextUpdateCommand()
        : this(new AiContextUpdateService())
    {
    }

    internal AiContextUpdateCommand(
        IAiContextUpdateService aiContextUpdateService_)
    {
        this._aiContextUpdateService =
            aiContextUpdateService_ ??
            throw new ArgumentNullException(
                nameof(aiContextUpdateService_));
    }

    public CommandResult Execute(
        BootstrapOptions options_)
    {
        string repoRoot =
            Path.GetFullPath(
                options_.RepoPath);

        AiContextUpdateRunResult result;

        try
        {
            result =
                this._aiContextUpdateService.Run(
                    repoRoot,
                    new AiContextUpdateOptions
                    {
                        TargetFramework =
                            options_.TargetFramework,
                        McpServerName =
                            options_.McpServerName,
                        McpProjectRelativePath =
                            options_.McpProjectRelativePath
                    });
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

        string markdown =
            string.Join(
                Environment.NewLine,
                [
                    "# AI Context Update",
                    "",
                    $"- Repo: `{repoRoot}`",
                    "- Status: `Passed`",
                    "- Manifest: `.ai/manifests/mcp-context-manifest.json`",
                    "- Summary: `.ai/generated/summaries/generated-context-summary.md`"
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
                "# AI Context Update",
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
