using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class ConfigsCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        RepoAnalysis analysis = new RepoDetector().Analyze(options_.RepoPath);
        IReadOnlyList<PlannedChange> changes = new ConfigGenerator().PlanConfigChanges(analysis, options_);
        string markdown = new ReportWriter().WritePlan(analysis, changes, "Configs Plan", options_);
        return CommandResult.Ok(markdown);
    }
}
