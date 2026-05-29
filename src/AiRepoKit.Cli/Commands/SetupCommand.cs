using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class SetupCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        bool apply = options_.Apply && !options_.DryRun;
        RepoDetection detection = new RepoDetector().Detect(options_.RepoPath);
        List<string> warnings = [];
        List<string> errors = [];
        List<string> phases = [];
        List<CommandResult> results = [];

        BootstrapOptions baseOptions = options_.With(includeMcp_: true, includeAgents_: true, backup_: apply ? true : options_.Backup);

        progress.StartPhase("Planning setup");
        phases.Add("detect");
        CommandResult plan = new PlanCommand().Execute(baseOptions.With(command_: "plan", apply_: false, dryRun_: true));
        results.Add(plan);
        progress.CompletePhase("Setup plan completed");

        if (apply)
        {
            progress.StartPhase("Running bootstrap");
            phases.Add("bootstrap");
            CommandResult bootstrap = new BootstrapCommand().Execute(CreateBootstrapOptions(baseOptions, options_));
            results.Add(bootstrap);
            progress.CompletePhase("Bootstrap phase completed");
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

            progress.StartPhase("Generating changed-files context pack");
            phases.Add("context-pack changed-files");
            RunOptionalContextPack(baseOptions, "changed-files", results, warnings);
            progress.CompletePhase("Changed-files context pack phase completed");
            phases.Add("context-pack review-risk");
            progress.StartPhase("Generating review-risk context pack");
            RunOptionalContextPack(baseOptions, "review-risk", results, warnings);
            progress.CompletePhase("Review-risk context pack phase completed");
            phases.Add("context-pack test-generation");
            progress.StartPhase("Generating test-generation context pack");
            RunOptionalContextPack(baseOptions, "test-generation", results, warnings);
            progress.CompletePhase("Test-generation context pack phase completed");

            progress.StartPhase("Generating graph baseline");
            phases.Add("graph baseline");
            CommandResult graph = new GraphCommand().Execute(baseOptions.With(command_: "graph", apply_: true, dryRun_: false));
            results.Add(graph);
            if (!graph.Success)
            {
                warnings.Add("Graph baseline could not be generated.");
            }
            progress.CompletePhase("Graph baseline phase completed");

            phases.Add("self-check");
            progress.StartPhase("Running self-check");
            CommandResult selfCheck = new SelfCheckCommand().Execute(CreateSetupSelfCheckOptions(baseOptions, options_));
            results.Add(selfCheck);
            if (!selfCheck.Success)
            {
                warnings.Add("self-check completed with failures or warnings; review its report.");
            }
            progress.CompletePhase("Self-check phase completed");

            phases.Add("mcp-diagnose");
            progress.StartPhase("Running MCP diagnostics");
            CommandResult diagnose = new McpDiagnoseCommand().Execute(CreateSetupMcpDiagnoseOptions(baseOptions, options_));
            results.Add(diagnose);
            if (!diagnose.Success)
            {
                warnings.Add("mcp-diagnose completed with failures; MCP may still need client reload or unlocked DLL.");
            }
            progress.CompletePhase("MCP diagnostics phase completed");
        }
        else
        {
            phases.Add("preview self-check");
            progress.StartPhase("Running preview self-check");
            CommandResult previewSelfCheck = new SelfCheckCommand().Execute(CreateSetupSelfCheckOptions(baseOptions, options_));
            results.Add(previewSelfCheck);
            if (!previewSelfCheck.Success)
            {
                warnings.Add($"Self Check preview returned exit code {previewSelfCheck.ExitCode}. Run `airepo self-check` for details.");
            }
            progress.CompletePhase("Preview self-check completed");
            phases.Add("preview mcp-diagnose");
            progress.StartPhase("Running preview MCP diagnostics");
            CommandResult previewMcp = new McpDiagnoseCommand().Execute(CreateSetupMcpDiagnoseOptions(baseOptions, options_));
            results.Add(previewMcp);
            if (!previewMcp.Success)
            {
                warnings.Add($"MCP Diagnose preview returned exit code {previewMcp.ExitCode}. Run `airepo mcp-diagnose` for details.");
            }
            progress.CompletePhase("Preview MCP diagnostics completed");
        }

        bool success = errors.Count == 0;
        CommandTimingReport? timingReport = options_.Timings ? progress.GetTimingReport() : null;
        string markdown = WriteReport(options_, detection, apply, phases, results, warnings, errors, plan.Markdown, timingReport);
        return new CommandResult(success, markdown, success ? 0 : 1);
    }

    private static BootstrapOptions CreateBootstrapOptions(BootstrapOptions baseOptions_, BootstrapOptions setupOptions_)
    {
        if (setupOptions_.Strict)
        {
            return baseOptions_.With(command_: "bootstrap", apply_: true, dryRun_: false, backup_: true);
        }

        return baseOptions_.With(command_: "bootstrap", apply_: true, dryRun_: false, backup_: true, skipBudget_: true);
    }

    private static BootstrapOptions CreateSetupSelfCheckOptions(BootstrapOptions baseOptions_, BootstrapOptions setupOptions_)
    {
        if (setupOptions_.Strict)
        {
            return baseOptions_.With(command_: "self-check", requireContextPacks_: true);
        }

        return baseOptions_.With(command_: "self-check", skipBuildMcp_: true, skipCodeInventory_: true, skipBudget_: true, skipAudit_: true, requireContextPacks_: true);
    }

    private static BootstrapOptions CreateSetupMcpDiagnoseOptions(BootstrapOptions baseOptions_, BootstrapOptions setupOptions_)
    {
        if (setupOptions_.Strict)
        {
            return baseOptions_.With(command_: "mcp-diagnose");
        }

        return baseOptions_.With(command_: "mcp-diagnose", skipBuildMcp_: true, skipBudget_: true);
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
        string planMarkdown_,
        CommandTimingReport? timings_)
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

        if (!options_.Summary)
        {
            builder.AppendLine();
            builder.AppendLine("## Planned Changes Summary");
            AppendSummary(builder, planMarkdown_);
            builder.AppendLine();
        }

        builder.AppendLine("## Warnings");
        AppendMessages(builder, warnings_);
        builder.AppendLine();
        builder.AppendLine("## Errors");
        AppendMessages(builder, errors_);
        if (options_.Timings && timings_ is not null)
        {
            builder.AppendLine();
            builder.AppendLine("## Timings");
            builder.AppendLine();
            builder.AppendLine($"- Total: `{timings_.TotalElapsedMilliseconds} ms`");
            foreach (CommandPhaseTiming phase in timings_.Phases)
            {
                builder.AppendLine($"- {phase.Name}: `{phase.ElapsedMilliseconds} ms` ({phase.Status})");
            }
        }

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
