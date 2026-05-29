using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ManagedFiles;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.ManagedFiles;

namespace AiRepoKit.Cli.Commands;

public sealed class InitCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        RepoAnalysis analysis = new RepoDetector().Analyze(options_.RepoPath);
        ConfigGenerator configGenerator = new();
        TemplateService templateService = new();
        FileSystemService fileSystemService = new();
        ManagedFilesService managedFilesService = new();
        List<PlannedChange> changes = [.. configGenerator.PlanInitChanges(analysis, options_)];
        List<string> errors = [];
        ManagedFilesManifest manifest = managedFilesService.Load(analysis.Profile.RootPath);
        bool manifestChanged = false;

        if (options_.Apply && !options_.DryRun)
        {
            foreach (PlannedChange change in changes.Where(change_ => change_.WillWrite))
            {
                try
                {
                    if (string.Equals(change.Path, ".gitignore", StringComparison.OrdinalIgnoreCase))
                    {
                        new GitIgnoreService().EnsureSection(analysis.Profile.RootPath, options_);
                        continue;
                    }

                    if (change.UpdateAction is not UpdateAction.Create and not UpdateAction.SafeUpdate and not UpdateAction.AppendSection)
                    {
                        continue;
                    }

                    string templatePath = string.IsNullOrWhiteSpace(change.TemplateId)
                        ? configGenerator.GetTemplatePathForDestination(change.Path)
                        : change.TemplateId;
                    if (string.IsNullOrWhiteSpace(templatePath))
                    {
                        fileSystemService.EnsureDirectory(analysis.Profile.RootPath, change.Path, options_.DryRun);
                        continue;
                    }

                    string content = templateService.RenderTemplate(templatePath, analysis, options_);
                    fileSystemService.WriteFile(
                        analysis.Profile.RootPath,
                        change.Path,
                        content,
                        options_,
                        change.UpdateAction == UpdateAction.SafeUpdate);
                    manifest = managedFilesService.AddOrUpdate(
                        manifest,
                        analysis.Profile.RootPath,
                        change.Path,
                        templatePath,
                        string.IsNullOrWhiteSpace(change.TemplateVersion) ? managedFilesService.GetToolVersion() : change.TemplateVersion,
                        content,
                        change.UpdateAction.ToString());
                    manifestChanged = true;
                }
                catch (Exception exception)
                {
                    errors.Add($"{change.Path}: {exception.Message}");
                }
            }
        }

        if (manifestChanged)
        {
            managedFilesService.Save(analysis.Profile.RootPath, manifest);
        }

        foreach (string error in errors)
        {
            changes.Add(new PlannedChange(ChangeType.Error, ".", error, false, false, false, false, "Write failed."));
        }

        string title = options_.Apply && !options_.DryRun ? "Init Apply" : "Init Dry Run";
        string markdown = new ReportWriter().WritePlan(analysis, changes, title, options_);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }
}
