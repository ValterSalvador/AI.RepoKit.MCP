using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class DoctorCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        RepoAnalysis analysis = new RepoDetector().Analyze(options_.RepoPath);
        TemplateService templateService = new();
        string templatesRoot = string.Empty;
        int templatesFound = 0;
        bool templatesAvailable = false;
        string templatesError = string.Empty;

        try
        {
            templatesRoot = templateService.GetTemplateRoot();
            templatesFound = templateService.ListTemplates().Count;
            templatesAvailable = templatesFound > 0;
        }
        catch (InvalidOperationException exception)
        {
            templatesError = exception.Message;
        }

        string markdown = new ReportWriter().WriteDoctor(
            analysis,
            options_,
            new GitService(),
            new FileSystemService(),
            templatesRoot,
            templatesFound,
            templatesAvailable,
            templatesError);
        return CommandResult.Ok(markdown);
    }
}
