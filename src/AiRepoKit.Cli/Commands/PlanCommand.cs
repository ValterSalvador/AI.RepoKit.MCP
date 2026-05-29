using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class PlanCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        RepoAnalysis analysis = new RepoDetector().Analyze(options_.RepoPath);
        IReadOnlyList<PlannedChange> changes = new ConfigGenerator().PlanInitChanges(analysis, options_);
        string markdown = new ReportWriter().WritePlan(analysis, changes, "Plan", options_);
        return CommandResult.Ok(markdown);
    }
}
