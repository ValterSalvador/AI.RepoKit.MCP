using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.BuildDiagnostics;

namespace AiRepoKit.Cli.Commands;

public sealed class BuildDiagnosticsCommand
{
    private readonly IBuildDiagnosticsService _buildDiagnosticsService;

    public BuildDiagnosticsCommand()
        : this(
            new BuildDiagnosticsService())
    {
    }

    internal BuildDiagnosticsCommand(
        IBuildDiagnosticsService buildDiagnosticsService)
    {
        _buildDiagnosticsService =
            buildDiagnosticsService ??
            throw new ArgumentNullException(
                nameof(buildDiagnosticsService));
    }

    public CommandResult Execute(
        BootstrapOptions options_)
    {
        try
        {
            BuildDiagnosticsRunResult result =
                _buildDiagnosticsService.Run(
                    options_.RepoPath);

            if (!result.Completed)
            {
                string detail =
                    string.IsNullOrWhiteSpace(
                        result.ErrorMessage)
                        ? "Unknown failure."
                        : result.ErrorMessage;

                return CommandResult.Failure(
                    "# Build Diagnostics Error" +
                    Environment.NewLine +
                    Environment.NewLine +
                    ProcessRunner.Redact(
                        detail),
                    result.ExitCode > 0
                        ? result.ExitCode
                        : 1);
            }

            if (result.Report is null)
            {
                return CommandResult.Failure(
                    "# Build Diagnostics Error" +
                    Environment.NewLine +
                    Environment.NewLine +
                    "Native build diagnostics completed without a report.",
                    1);
            }

            string markdown =
                WriteReport(
                    options_,
                    result.Report);

            return result.ExitCode == 0
                ? CommandResult.Ok(
                    markdown)
                : CommandResult.Failure(
                    markdown,
                    result.ExitCode);
        }
        catch (Exception exception)
        {
            return CommandResult.Failure(
                "# Build Diagnostics Error" +
                Environment.NewLine +
                Environment.NewLine +
                ProcessRunner.Redact(
                    exception.Message),
                1);
        }
    }

    private static string WriteReport(
        BootstrapOptions options_,
        BuildDiagnosticsReport report_)
    {
        StringBuilder builder =
            new();

        builder.AppendLine(
            "# Build Diagnostics");

        builder.AppendLine();

        builder.AppendLine(
            $"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");

        builder.AppendLine(
            $"- Target: `{ProcessRunner.Redact(report_.Target)}`");

        builder.AppendLine(
            $"- Restore exit code: `{report_.RestoreExitCode}`");

        builder.AppendLine(
            $"- Build exit code: `{report_.BuildExitCode}`");

        if (!string.IsNullOrWhiteSpace(
                report_.Status))
        {
            builder.AppendLine(
                $"- Status: {ProcessRunner.Redact(report_.Status)}");
        }

        builder.AppendLine(
            $"- Report: `{BuildDiagnosticsService.ReportRelativePath}`");

        builder.AppendLine(
            $"- Summary: `{BuildDiagnosticsService.SummaryRelativePath}`");

        return builder
            .ToString()
            .TrimEnd();
    }
}
