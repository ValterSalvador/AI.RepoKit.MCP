using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class SetupCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        bool apply = options_.Apply && !options_.DryRun;
        RepoDetection detection = new RepoDetector().Detect(options_.RepoPath);
        List<string> warnings = [];
        List<string> errors = [];
        List<string> phases = [];
        List<CommandResult> results = [];

        BootstrapOptions baseOptions = options_.With(includeMcp_: true, includeAgents_: true, backup_: apply ? true : options_.Backup);

        phases.Add("detect");
        CommandResult plan = new PlanCommand().Execute(baseOptions.With(command_: "plan", apply_: false, dryRun_: true));
        results.Add(plan);

        if (apply)
        {
            phases.Add("bootstrap");
            CommandResult bootstrap = new BootstrapCommand().Execute(baseOptions.With(command_: "bootstrap", apply_: true, dryRun_: false, backup_: true));
            results.Add(bootstrap);
            if (!bootstrap.Success)
            {
                if (CanDowngradeLockedDllFailure(options_, bootstrap, warnings))
                {
                    warnings.Add("MCP DLL appears to be in use, but non-strict setup continued after MCP diagnostics passed.");
                }
                else
                {
                    errors.Add("Bootstrap failed.");
                }
            }

            phases.Add("context-pack review-risk");
            RunOptionalContextPack(baseOptions, "review-risk", results, warnings);
            phases.Add("context-pack test-generation");
            RunOptionalContextPack(baseOptions, "test-generation", results, warnings);

            phases.Add("self-check");
            CommandResult selfCheck = new SelfCheckCommand().Execute(baseOptions.With(command_: "self-check", requireContextPacks_: true));
            results.Add(selfCheck);
            if (!selfCheck.Success)
            {
                warnings.Add("self-check completed with failures or warnings; review its report.");
            }

            phases.Add("mcp-diagnose");
            CommandResult diagnose = new McpDiagnoseCommand().Execute(baseOptions.With(command_: "mcp-diagnose"));
            results.Add(diagnose);
            if (!diagnose.Success)
            {
                warnings.Add("mcp-diagnose completed with failures; MCP may still need client reload or unlocked DLL.");
            }
        }
        else
        {
            phases.Add("preview self-check");
            CommandResult previewSelfCheck = new SelfCheckCommand().Execute(baseOptions.With(command_: "self-check", skipBuildMcp_: true, skipCodeInventory_: true, skipBudget_: true, skipAudit_: true, requireContextPacks_: true));
            results.Add(previewSelfCheck);
            phases.Add("preview mcp-diagnose");
            CommandResult previewMcp = new McpDiagnoseCommand().Execute(baseOptions.With(command_: "mcp-diagnose", skipBuildMcp_: true));
            results.Add(previewMcp);
        }

        bool success = errors.Count == 0;
        string markdown = WriteReport(options_, detection, apply, phases, results, warnings, errors, plan.Markdown);
        return new CommandResult(success, markdown, success ? 0 : 1);
    }

    private static void RunOptionalContextPack(BootstrapOptions options_, string task_, List<CommandResult> results_, List<string> warnings_)
    {
        CommandResult result = new ContextPackCommand().Execute(options_.With(command_: "context-pack", apply_: true, dryRun_: false, task_: task_));
        results_.Add(result);
        if (!result.Success)
        {
            warnings_.Add($"Context pack `{task_}` could not be generated.");
        }
    }

    private static bool CanDowngradeLockedDllFailure(BootstrapOptions options_, CommandResult bootstrap_, List<string> warnings_)
    {
        if (options_.Strict || !bootstrap_.Markdown.Contains(McpBuildFailureDiagnostics.LockedDllMessage, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CommandResult diagnose = new McpDiagnoseCommand().Execute(options_.With(command_: "mcp-diagnose", skipBuildMcp_: true));
        if (diagnose.Success)
        {
            warnings_.Add("Locked MCP DLL build failure was downgraded because JSON-RPC diagnostics passed.");
            return true;
        }

        return false;
    }

    private static string WriteReport(
        BootstrapOptions options_,
        RepoDetection detection_,
        bool apply_,
        IReadOnlyList<string> phases_,
        IReadOnlyList<CommandResult> results_,
        IReadOnlyList<string> warnings_,
        IReadOnlyList<string> errors_,
        string planMarkdown_)
    {
        StringBuilder builder = new();
        builder.AppendLine(apply_ ? "# Setup Apply" : "# Setup Preview");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(apply_ ? "apply" : "dry-run")}`");
        builder.AppendLine($"- Profile: `{options_.Profile}`");
        builder.AppendLine($"- Detection profile: `{detection_.RecommendedProfile}` confidence `{detection_.Confidence:0.00}`");
        builder.AppendLine($"- Clients: `{string.Join(", ", ConfigGenerator.GetSelectedClients(options_).Select(ConfigGenerator.GetClientDisplayName))}`");
        builder.AppendLine($"- Defaults: `{options_.DefaultsSummary}`");
        builder.AppendLine();
        builder.AppendLine("## Phases");
        foreach (string phase in phases_)
        {
            builder.AppendLine($"- {phase}");
        }

        builder.AppendLine();
        builder.AppendLine("## Results");
        foreach (CommandResult result in results_)
        {
            string title = result.Markdown.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Command";
            builder.AppendLine($"- {title.TrimStart('#', ' ')}: exit `{result.ExitCode}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Planned Changes Summary");
        AppendSummary(builder, planMarkdown_);
        builder.AppendLine();
        builder.AppendLine("## Warnings");
        AppendMessages(builder, warnings_);
        builder.AppendLine();
        builder.AppendLine("## Errors");
        AppendMessages(builder, errors_);
        return builder.ToString().TrimEnd();
    }

    private static void AppendSummary(StringBuilder builder_, string markdown_)
    {
        string[] lines = markdown_.Split(Environment.NewLine);
        bool inSummary = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("## Summary", StringComparison.Ordinal))
            {
                inSummary = true;
                continue;
            }

            if (inSummary && line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            if (inSummary && line.StartsWith("- ", StringComparison.Ordinal))
            {
                builder_.AppendLine(line);
            }
        }
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
