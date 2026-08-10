using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class UpdateCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        bool apply = options_.Apply && !options_.DryRun;
        List<UpdatePhase> phases = [];

        Run("detect", () => new DetectCommand().Execute(options_.With(auditJson_: false)), phases, progress);

        BootstrapOptions generated = options_.With(
            apply_: apply,
            dryRun_: !apply,
            format_: "json",
            auditJson_: false);

        Run("code-index", () => new CodeIndexCommand().Execute(generated.With(
            command_: "code-index",
            rebuildCache_: options_.RebuildCache)), phases, progress);

        Run("context-pack changed-files", () => new ContextPackCommand().Execute(generated.With(
            command_: "context-pack",
            task_: "changed-files",
            target_: string.Empty,
            limit_: ResolveLimit(options_, 20),
            budget_: ResolveBudget(options_, 3000))), phases, progress);

        Run("impact changed-files", () => new ImpactCommand().Execute(generated.With(
            command_: "impact",
            target_: options_.Target,
            limit_: ResolveLimit(options_, 20),
            budget_: ResolveBudget(options_, 3000))), phases, progress);

        if (!options_.Quick)
        {
            Run("context-pack review-risk", () => new ContextPackCommand().Execute(generated.With(
                command_: "context-pack",
                task_: "review-risk",
                target_: options_.Target,
                limit_: ResolveLimit(options_, 15),
                budget_: ResolveBudget(options_, 4000))), phases, progress);

            string testTarget = string.IsNullOrWhiteSpace(options_.TestTarget) ? options_.Target : options_.TestTarget;
            Run("context-pack test-generation", () => new ContextPackCommand().Execute(generated.With(
                command_: "context-pack",
                task_: "test-generation",
                target_: testTarget,
                limit_: ResolveLimit(options_, 15),
                budget_: ResolveBudget(options_, 3000))), phases, progress);
        }

        Run("self-check", () => new SelfCheckCommand().Execute(options_.With(
            command_: "self-check",
            apply_: false,
            dryRun_: true,
            auditJson_: false,
            quick_: !options_.Full && !options_.Strict,
            full_: options_.Full,
            strict_: options_.Strict,
            skipBuildMcp_: !options_.Strict)), phases, progress);

        int exitCode = phases.Select(phase_ => phase_.ExitCode).DefaultIfEmpty(0).Max();
        string report = options_.AuditJson
            ? JsonSerializer.Serialize(new
            {
                command = "update",
                repo = Path.GetFullPath(options_.RepoPath),
                mode = apply ? "apply" : "dry-run",
                preset = options_.Quick ? "quick" : options_.Strict ? "strict" : options_.Full ? "full" : "default",
                target = options_.Target,
                testTarget = string.IsNullOrWhiteSpace(options_.TestTarget) ? options_.Target : options_.TestTarget,
                phases,
                exitCode
            }, JsonOptions)
            : WriteMarkdown(options_, apply, phases, progress.GetTimingReport());

        return new CommandResult(exitCode == 0, report, exitCode);
    }

    private static void Run(string name_, Func<CommandResult> action_, List<UpdatePhase> phases_, ProgressReporter progress_)
    {
        progress_.StartPhase("Running " + name_);
        try
        {
            CommandResult result = action_();
            phases_.Add(new UpdatePhase(name_, result.ExitCode, result.Success));
            if (result.Success)
            {
                progress_.CompletePhase(name_ + " completed");
            }
            else
            {
                progress_.FailPhase(name_ + " returned exit " + result.ExitCode);
            }
        }
        catch (Exception exception)
        {
            phases_.Add(new UpdatePhase(name_, 1, false, ProcessRunner.Redact(exception.Message)));
            progress_.FailPhase(name_ + " failed");
        }
    }

    private static int ResolveLimit(BootstrapOptions options_, int phaseDefault_)
    {
        return options_.Limit == 20 ? phaseDefault_ : options_.Limit;
    }

    private static int ResolveBudget(BootstrapOptions options_, int phaseDefault_)
    {
        return options_.Budget > 0 ? options_.Budget : phaseDefault_;
    }

    private static string WriteMarkdown(BootstrapOptions options_, bool apply_, IReadOnlyList<UpdatePhase> phases_, CommandTimingReport timings_)
    {
        StringBuilder builder = new();
        builder.AppendLine(apply_ ? "# Update Apply" : "# Update Preview");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Preset: `{(options_.Quick ? "quick" : options_.Strict ? "strict" : options_.Full ? "full" : "default")}`");
        builder.AppendLine($"- Target: `{(string.IsNullOrWhiteSpace(options_.Target) ? "none" : options_.Target)}`");
        builder.AppendLine($"- Test target: `{(string.IsNullOrWhiteSpace(options_.TestTarget) ? (string.IsNullOrWhiteSpace(options_.Target) ? "none" : options_.Target) : options_.TestTarget)}`");
        builder.AppendLine();
        builder.AppendLine("## Phases");
        builder.AppendLine();
        foreach (UpdatePhase phase in phases_)
        {
            builder.AppendLine($"- {phase.Name}: exit `{phase.ExitCode}`{(string.IsNullOrWhiteSpace(phase.Error) ? string.Empty : " - " + phase.Error)}");
        }

        if (options_.Timings)
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

        builder.AppendLine();
        builder.AppendLine(apply_
            ? "Generated context is up to date. Use `--dry-run` to preview without writing."
            : "Preview only. Run `airepo update` to write regenerable artifacts.");
        return builder.ToString().TrimEnd();
    }

    private sealed record UpdatePhase(string Name, int ExitCode, bool Success, string Error = "");
}
