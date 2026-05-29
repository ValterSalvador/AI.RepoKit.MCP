using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class SampleCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        string repoPath = Path.GetFullPath(options_.RepoPath);
        bool exists = Directory.Exists(repoPath);
        bool isEmpty = !exists || !Directory.EnumerateFileSystemEntries(repoPath).Any();
        List<string> planned = [];
        List<string> created = [];
        List<string> warnings = [];
        List<string> errors = [];
        List<ProcessResult> processes = [];

        planned.Add(repoPath);
        planned.Add(Path.Combine(repoPath, "SampleRepo.slnx"));
        planned.Add(Path.Combine(repoPath, "src", "Sample.Domain", "Sample.Domain.csproj"));
        planned.Add(Path.Combine(repoPath, "src", "Sample.Domain", "Class1.cs"));

        if (exists && !isEmpty && !options_.Force)
        {
            string message = "Target repository exists and is not empty. Use --force to allow sample creation in this path.";
            if (options_.Apply && !options_.DryRun)
            {
                errors.Add(message);
            }
            else
            {
                warnings.Add(message);
            }
        }

        if (options_.Apply && !options_.DryRun && errors.Count == 0)
        {
            Directory.CreateDirectory(repoPath);
            ProcessRunner runner = new();
            processes.Add(runner.Run("dotnet", ["new", "sln", "-n", "SampleRepo", .. ForceArguments(options_)], repoPath));

            if (processes.Last().Success)
            {
                processes.Add(runner.Run("dotnet", ["new", "classlib", "-n", "Sample.Domain", "-o", Path.Combine("src", "Sample.Domain"), .. ForceArguments(options_)], repoPath));
            }

            if (processes.All(process_ => process_.Success))
            {
                processes.Add(runner.Run("dotnet", ["sln", "add", Path.Combine("src", "Sample.Domain", "Sample.Domain.csproj")], repoPath));
            }

            foreach (ProcessResult process in processes.Where(process_ => !process_.Success))
            {
                errors.Add($"{process.FileName} {process.Arguments} failed with exit code {process.ExitCode}.");
            }

            if (errors.Count == 0)
            {
                created.Add(repoPath);
                created.AddRange(Directory.EnumerateFiles(repoPath, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase));
            }
        }

        string markdown = WriteReport(options_, repoPath, planned, created, warnings, errors, processes);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }

    private static IEnumerable<string> ForceArguments(BootstrapOptions options_)
    {
        if (options_.Force)
        {
            yield return "--force";
        }
    }

    private static string WriteReport(
        BootstrapOptions options_,
        string repoPath_,
        IReadOnlyList<string> planned_,
        IReadOnlyList<string> created_,
        IReadOnlyList<string> warnings_,
        IReadOnlyList<string> errors_,
        IReadOnlyList<ProcessResult> processes_)
    {
        StringBuilder builder = new();
        builder.AppendLine(options_.Apply && !options_.DryRun ? "# Sample Apply" : "# Sample Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{repoPath_}`");
        builder.AppendLine($"- Mode: `{(options_.Apply && !options_.DryRun ? "apply" : "dry-run")}`");
        builder.AppendLine($"- Force: `{options_.Force}`");
        builder.AppendLine();
        builder.AppendLine("## Planned Files");
        builder.AppendLine();
        foreach (string path in planned_)
        {
            builder.AppendLine($"- `{path}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Files Created");
        builder.AppendLine();
        if (created_.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (string path in created_)
            {
                builder.AppendLine($"- `{path}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Commands");
        builder.AppendLine();
        if (processes_.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (ProcessResult process in processes_)
            {
                builder.AppendLine($"- `{process.FileName} {process.Arguments}` exit `{process.ExitCode}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Warnings");
        builder.AppendLine();
        AppendMessages(builder, warnings_);
        builder.AppendLine();
        builder.AppendLine("## Errors");
        builder.AppendLine();
        AppendMessages(builder, errors_);
        return builder.ToString().TrimEnd();
    }

    private static void AppendMessages(StringBuilder builder_, IReadOnlyList<string> messages_)
    {
        if (messages_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string message in messages_)
        {
            builder_.AppendLine($"- {ProcessRunner.Redact(message)}");
        }
    }
}
