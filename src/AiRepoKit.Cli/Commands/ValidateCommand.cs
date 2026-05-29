using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class ValidateCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        RepoAnalysis analysis = new RepoDetector().Analyze(options_.RepoPath);
        string markdown = new ReportWriter().WriteValidation(analysis);
        return CommandResult.Ok(markdown);
    }
}
