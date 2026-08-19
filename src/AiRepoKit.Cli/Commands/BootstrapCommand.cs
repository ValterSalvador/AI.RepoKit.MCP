using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ManagedFiles;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.ManagedFiles;
using AiRepoKit.Cli.Services.McpBudget;
using AiRepoKit.Cli.Services.SdkAlignment;

namespace AiRepoKit.Cli.Commands;

public sealed class BootstrapCommand
{
    private readonly IScriptRunner _scriptRunner;
    private readonly IMcpBudgetService _mcpBudgetService;
    private readonly ISdkAlignmentService _sdkAlignmentService;

    public BootstrapCommand()
        : this(
            ScriptRuntimeFactory.CreateDefault(),
            new McpBudgetService(),
            new SdkAlignmentService())
    {
    }

    /// <summary>
    /// Internal constructor for testing. Optional native services preserve
    /// compatibility with existing tests that inject only a script runner.
    /// </summary>
    internal BootstrapCommand(
        IScriptRunner scriptRunner,
        IMcpBudgetService? mcpBudgetService = null,
        ISdkAlignmentService? sdkAlignmentService = null)
    {
        _scriptRunner = scriptRunner ?? throw new ArgumentNullException(nameof(scriptRunner));
        _mcpBudgetService = mcpBudgetService ?? new McpBudgetService();
        _sdkAlignmentService = sdkAlignmentService ?? new SdkAlignmentService();
    }

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        List<string> warnings = [];
        List<string> errors = [];
        List<ProcessResult> processes = [];
        bool apply = options_.Apply && !options_.DryRun;

        progress.StartPhase("Running doctor");
        CommandResult doctor = new DoctorCommand().Execute(options_);
        progress.CompletePhase("Doctor completed");
        progress.StartPhase("Planning changes");
        CommandResult plan = new PlanCommand().Execute(options_);
        progress.CompletePhase("Planning completed");
        progress.StartPhase("Writing files");
        CommandResult init = new InitCommand().Execute(options_);
        progress.CompletePhase(apply ? "File writing completed" : "File planning completed");
        progress.StartPhase("Validating generated files");
        CommandResult validate = new ValidateCommand().Execute(options_);
        progress.CompletePhase("Validation completed");

        string hooksStatus = "Skipped by --no-hooks";
        if (!options_.SkipHooks)
        {
            progress.StartPhase(apply ? "Installing Git hooks" : "Previewing Git hooks");
            CommandResult hooks = new HooksCommand().Execute(options_.With(command_: "hooks", apply_: apply, dryRun_: !apply));
            hooksStatus = hooks.Success ? (apply ? "Installed" : "Planned") : $"Warning exit {hooks.ExitCode}";
            if (hooks.Success)
            {
                progress.CompletePhase(apply ? "Git hooks installed" : "Git hooks preview completed");
            }
            else
            {
                warnings.Add("Git hooks could not be configured. Run airepo hooks --apply for details or use --no-hooks to opt out.");
                progress.WarnPhase("Git hooks completed with warnings");
            }
        }

        string codeIndexStatus = "Skipped";
        bool codeIndexPassed = false;

        if (!doctor.Success)
        {
            errors.Add("Doctor failed.");
        }

        if (!init.Success)
        {
            errors.Add("Init failed.");
        }

        if (!validate.Success)
        {
            errors.Add("Validate failed.");
        }

        if (options_.IncludeMcp && !options_.SkipCodeInventory)
        {
            if (apply)
            {
                progress.StartPhase("Running code index");
                CommandResult codeIndex = new CodeIndexCommand().Execute(options_);
                codeIndexPassed = codeIndex.Success;
                codeIndexStatus = codeIndex.Success ? "Passed" : $"Warning exit {codeIndex.ExitCode}";
                if (!codeIndex.Success)
                {
                    warnings.Add("RoslynLite code-index failed. PowerShell inventory fallback will run if the script exists.");
                    progress.WarnPhase("Code index completed with warnings");
                }
                else
                {
                    progress.CompletePhase("Code index completed");
                }
            }
            else
            {
                codeIndexStatus = "Simulated";
            }
        }

        string mcpBuildStatus = "Skipped";
        if (options_.IncludeMcp && !options_.SkipBuildMcp)
        {
            if (apply)
            {
                string mcpProjectPath = Path.Combine(Path.GetFullPath(options_.RepoPath), options_.McpProjectRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(mcpProjectPath))
                {
                    progress.StartPhase("Building MCP project");
                    McpBuildService buildService = new();
                    McpBuildResult build = buildService.Execute(options_);
                    if (build.Process is not null)
                    {
                        processes.Add(build.Process);
                    }

                    if (build.State == "Failed" && build.Process is not null && McpBuildFailureDiagnostics.IsLockedDllFailure(build.Process) && !options_.Strict)
                    {
                        var smoke = new McpSmokeTestService().Run(options_.RepoPath, build.DllPath, options_.Verbose);
                        if (smoke.Success)
                        {
                            build = McpBuildService.CreateLockedSmokePassed(build);
                            warnings.Add("Locked MCP DLL build failure was downgraded because JSON-RPC smoke test passed.");
                        }
                    }

                    mcpBuildStatus = build.State;
                    if (build.State == "Failed")
                    {
                        progress.FailPhase("MCP project build failed");
                        if (build.Process is not null && McpBuildFailureDiagnostics.IsLockedDllFailure(build.Process))
                        {
                            errors.Add(McpBuildFailureDiagnostics.LockedDllMessage);
                            warnings.Add(McpBuildFailureDiagnostics.LockedDllHint);
                        }
                        else
                        {
                            errors.Add(build.Message);
                        }
                    }
                    else
                    {
                        progress.CompletePhase("MCP project build completed");
                    }
                }
                else
                {
                    mcpBuildStatus = "Missing project";
                    warnings.Add($"MCP project was not found at {options_.McpProjectRelativePath}.");
                }
            }
            else
            {
                mcpBuildStatus = "Simulated";
            }
        }

        IReadOnlyList<ScriptPlan> scripts = GetScripts(options_, codeIndexPassed);
        HashSet<string> scriptRefreshEligiblePaths = apply
            ? GetCleanManagedScriptRefreshPaths(options_)
            : [];
        List<string> scriptStatuses = [];
        bool updateAiContextPassed = false;
        if (options_.SkipScripts)
        {
            scriptStatuses.Add("All scripts skipped.");
            scriptStatuses.Add("sdk-alignment: Skipped by --skip-scripts");
        }
        else if (options_.IncludeMcp && (mcpBuildStatus is "Built" or "SkippedCurrent" or "SkippedLockedSmokePassed" || !apply))
        {
            if (options_.SkipAiContext)
            {
                scriptStatuses.Add("sdk-alignment: Skipped by --skip-ai-context");
            }

            foreach (ScriptPlan script in scripts)
            {
                string scriptDisplayPath = script.Definition.PowerShellRelativePath ?? script.Definition.Name;
                bool isUpdateAiContext = string.Equals(
                    script.Definition.Name,
                    "update-ai-context",
                    StringComparison.Ordinal);

                if (apply)
                {
                    progress.StartPhase($"Running AI context script {scriptDisplayPath}");

                    bool isPowerShellTarget = options_.ScriptShell is ScriptShell.Auto or ScriptShell.PowerShell;
                    string? expectedPath = isPowerShellTarget ? script.Definition.PowerShellRelativePath : script.Definition.BashRelativePath;
                    bool isPhysicalMissing = expectedPath != null && !File.Exists(Path.Combine(Path.GetFullPath(options_.RepoPath), expectedPath.Replace('/', Path.DirectorySeparatorChar)));

                    if (isPhysicalMissing)
                    {
                        scriptStatuses.Add($"{scriptDisplayPath}: Missing");
                        warnings.Add($"{scriptDisplayPath} was not found.");
                        progress.WarnPhase($"AI context script missing: {scriptDisplayPath}");
                    }
                    else
                    {
                        try
                        {
                            ProcessResult scriptResult = _scriptRunner.RunScript(
                                script.Definition,
                                options_.ScriptShell,
                                Path.GetFullPath(options_.RepoPath));

                            processes.Add(scriptResult);
                            scriptStatuses.Add($"{scriptDisplayPath}: {(scriptResult.Success ? "Passed" : $"Failed exit {scriptResult.ExitCode}")}");

                            if (scriptResult.Success && string.Equals(script.Definition.Name, "update-ai-context", StringComparison.Ordinal))
                            {
                                updateAiContextPassed = true;
                            }

                            if (!scriptResult.Success)
                            {
                                errors.Add($"{scriptDisplayPath} failed.");
                                progress.FailPhase($"AI context script failed: {scriptDisplayPath}");
                            }
                            else
                            {
                                progress.CompletePhase($"AI context script completed: {scriptDisplayPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            scriptStatuses.Add($"{scriptDisplayPath}: Failed / unable to execute");
                            errors.Add(ProcessRunner.Redact($"{scriptDisplayPath} execution failed: {ex.Message}"));
                            progress.FailPhase($"AI context script failed: {scriptDisplayPath}");
                        }
                    }
                }
                else
                {
                    scriptStatuses.Add($"{scriptDisplayPath}: Simulated");
                }

                if (isUpdateAiContext)
                {
                    if (apply)
                    {
                        RunSdkAlignment(
                            options_,
                            progress,
                            scriptStatuses,
                            errors);
                    }
                    else
                    {
                        scriptStatuses.Add("sdk-alignment: Simulated");
                    }
                }
            }
        }
        else if (options_.IncludeMcp)
        {
            scriptStatuses.Add("Skipped because MCP build did not pass.");
        }
        else
        {
            scriptStatuses.Add("Skipped because --mcp was not selected.");
        }

        if (apply && updateAiContextPassed)
        {
            RefreshManagedManifestForScriptOutputs(options_, scriptRefreshEligiblePaths);
        }

        // ── Native MCP budget (IMcpBudgetService) ──────────────────────────────
        // mcp-budget is no longer in GetScripts(); it runs as a separate native step.
        string budgetStatus = "Skipped";
        if (!options_.SkipBudget && options_.IncludeMcp)
        {
            if (apply)
            {
                progress.StartPhase("Running MCP budget");
                try
                {
                    McpBudgetRunResult budgetResult = _mcpBudgetService.Run(Path.GetFullPath(options_.RepoPath));
                    if (budgetResult.IsSuccess)
                    {
                        budgetStatus = "Passed";
                        progress.CompletePhase("MCP budget completed");
                    }
                    else
                    {
                        budgetStatus = $"Failed (exit {(int)budgetResult.ExitClass})";
                        errors.Add($"MCP budget validation failed: {string.Join("; ", budgetResult.Report.Failures.Take(3))}");
                        progress.FailPhase("MCP budget failed");
                    }
                }
                catch (Exception ex)
                {
                    budgetStatus = "Failed";
                    errors.Add(ProcessRunner.Redact($"MCP budget execution failed: {ex.Message}"));
                    progress.FailPhase("MCP budget failed");
                }
            }
            else
            {
                budgetStatus = "Simulated";
            }
        }

        scriptStatuses.Add($"mcp-budget: {budgetStatus}");

        if (errors.Count == 0)
        {
            progress.CompletePhase("Bootstrap completed");
        }
        else
        {
            progress.FailPhase("Bootstrap completed with errors");
        }
        string markdown = WriteReport(options_, doctor, plan, init, validate, codeIndexStatus, mcpBuildStatus, hooksStatus, scriptStatuses, warnings, errors, processes);
        return errors.Count == 0 ? CommandResult.Ok(markdown) : CommandResult.Failure(markdown, 1);
    }

    private void RunSdkAlignment(
        BootstrapOptions options_,
        ProgressReporter progress_,
        List<string> statuses_,
        List<string> errors_)
    {
        progress_.StartPhase("Running native SDK alignment");

        try
        {
            SdkAlignmentRunResult result = _sdkAlignmentService.Run(
                Path.GetFullPath(options_.RepoPath));

            if (result.IsSuccess)
            {
                statuses_.Add("sdk-alignment: Passed");
                progress_.CompletePhase("Native SDK alignment completed");
            }
            else
            {
                statuses_.Add("sdk-alignment: Failed");
                errors_.Add(
                    ProcessRunner.Redact(
                        $"SDK alignment failed: {result.ErrorMessage ?? "Unknown failure."}"));
                progress_.FailPhase("Native SDK alignment failed");
            }
        }
        catch (Exception ex)
        {
            statuses_.Add("sdk-alignment: Failed / unable to execute");
            errors_.Add(
                ProcessRunner.Redact(
                    $"SDK alignment execution failed: {ex.Message}"));
            progress_.FailPhase("Native SDK alignment failed");
        }
    }

    private static HashSet<string> GetCleanManagedScriptRefreshPaths(BootstrapOptions options_)
    {
        ManagedFilesService managedFilesService = new();
        ContentHashService contentHashService = new();
        string rootPath = Path.GetFullPath(options_.RepoPath);
        ManagedFilesManifest manifest = managedFilesService.Load(rootPath);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in GetScriptManagedRefreshPaths())
        {
            string normalizedPath = managedFilesService.NormalizeRelativePath(rootPath, path);
            ManagedFileEntry? entry = manifest.Files.FirstOrDefault(file_ => string.Equals(file_.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
            string fullPath = Path.Combine(rootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (entry is null || !File.Exists(fullPath))
            {
                continue;
            }

            string currentHash = contentHashService.ComputeSha256(File.ReadAllText(fullPath));
            if (string.Equals(currentHash, entry.LastGeneratedHash, StringComparison.Ordinal))
            {
                paths.Add(normalizedPath);
            }
        }

        return paths;
    }

    private static void RefreshManagedManifestForScriptOutputs(BootstrapOptions options_, HashSet<string> eligiblePaths_)
    {
        if (eligiblePaths_.Count == 0)
        {
            return;
        }

        ManagedFilesService managedFilesService = new();
        ConfigGenerator configGenerator = new();
        string rootPath = Path.GetFullPath(options_.RepoPath);
        ManagedFilesManifest manifest = managedFilesService.Load(rootPath);
        bool changed = false;
        foreach (string normalizedPath in eligiblePaths_)
        {
            string fullPath = Path.Combine(rootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                continue;
            }

            string templatePath = configGenerator.GetTemplatePathForDestination(normalizedPath);
            if (string.IsNullOrWhiteSpace(templatePath))
            {
                continue;
            }

            manifest = managedFilesService.AddOrUpdate(
                manifest,
                rootPath,
                normalizedPath,
                templatePath,
                managedFilesService.GetToolVersion(),
                File.ReadAllText(fullPath),
                "ScriptUpdate");
            changed = true;
        }

        if (changed)
        {
            managedFilesService.Save(rootPath, manifest);
        }
    }

    private static IReadOnlyList<string> GetScriptManagedRefreshPaths()
    {
        return [".ai/manifests/mcp-context-manifest.json"];
    }

    private static readonly ScriptDefinition UpdateAiContextScript = new(
        "update-ai-context",
        PowerShellRelativePath: "Tools/AiContext/UpdateAiContext.ps1",
        BashRelativePath: null);

    private static readonly ScriptDefinition UpdateCodeInventoryScript = new(
        "update-code-inventory",
        PowerShellRelativePath: "Tools/AiContext/UpdateCodeInventory.ps1",
        BashRelativePath: null);

    private static readonly ScriptDefinition CheckSecretsScript = new(
        "check-secrets",
        PowerShellRelativePath: "Tools/AiContext/CheckSecrets.ps1",
        BashRelativePath: null);

    private static IReadOnlyList<ScriptPlan> GetScripts(BootstrapOptions options_, bool codeIndexPassed_)
    {
        List<ScriptPlan> scripts = [];
        if (!options_.SkipAiContext)
        {
            scripts.Add(new ScriptPlan(UpdateAiContextScript));
        }

        if (!options_.SkipCodeInventory && !codeIndexPassed_)
        {
            scripts.Add(new ScriptPlan(UpdateCodeInventoryScript));
        }

        if (!options_.SkipSecurityScan)
        {
            scripts.Add(new ScriptPlan(CheckSecretsScript));
        }

        // Note: mcp-budget is intentionally absent — it runs as a native IMcpBudgetService
        // step separately from the script runner loop (P02.1).

        return scripts;
    }

    private static string WriteReport(
        BootstrapOptions options_,
        CommandResult doctor_,
        CommandResult plan_,
        CommandResult init_,
        CommandResult validate_,
        string codeIndexStatus_,
        string mcpBuildStatus_,
        string hooksStatus_,
        IReadOnlyList<string> scriptStatuses_,
        IReadOnlyList<string> warnings_,
        IReadOnlyList<string> errors_,
        IReadOnlyList<ProcessResult> processes_)
    {
        StringBuilder builder = new();
        builder.AppendLine(options_.Apply && !options_.DryRun ? "# Bootstrap Apply" : "# Bootstrap Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Mode: `{(options_.Apply && !options_.DryRun ? "apply" : "dry-run")}`");
        builder.AppendLine($"- MCP: `{options_.IncludeMcp}`");
        builder.AppendLine($"- Profile: `{options_.Profile}`");
        builder.AppendLine($"- Selected clients: `{string.Join(", ", ConfigGenerator.GetSelectedClients(options_).Select(ConfigGenerator.GetClientDisplayName))}`");
        builder.AppendLine();
        builder.AppendLine("## Doctor Status");
        builder.AppendLine();
        builder.AppendLine($"- ExitCode: `{doctor_.ExitCode}`");
        builder.AppendLine();
        builder.AppendLine("## Planned Changes");
        builder.AppendLine();
        AppendSummary(builder, plan_.Markdown);
        builder.AppendLine();
        builder.AppendLine("## Files Written");
        builder.AppendLine();
        AppendSummary(builder, init_.Markdown);
        builder.AppendLine();
        builder.AppendLine("## Validate Status");
        builder.AppendLine();
        builder.AppendLine($"- ExitCode: `{validate_.ExitCode}`");
        builder.AppendLine();
        builder.AppendLine("## Code Index Status");
        builder.AppendLine();
        builder.AppendLine($"- {codeIndexStatus_}");
        builder.AppendLine();
        builder.AppendLine("## MCP Build Status");
        builder.AppendLine();
        builder.AppendLine($"- {mcpBuildStatus_}");
        builder.AppendLine();
        builder.AppendLine("## Git Hooks Status");
        builder.AppendLine();
        builder.AppendLine($"- {hooksStatus_}");
        builder.AppendLine();
        builder.AppendLine("## Script Execution Status");
        builder.AppendLine();
        AppendMessages(builder, scriptStatuses_);
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
                if (!process.Success)
                {
                    AppendProcessText(builder, "stdout", process.StandardOutput);
                    AppendProcessText(builder, "stderr", process.StandardError);
                }
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

    private static void AppendSummary(StringBuilder builder_, string markdown_)
    {
        string[] lines = ProcessRunner.Redact(markdown_).Split(Environment.NewLine);
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

        if (!inSummary)
        {
            builder_.AppendLine($"- ExitCode: `{0}`");
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

    private static void AppendProcessText(StringBuilder builder_, string label_, string value_)
    {
        if (string.IsNullOrWhiteSpace(value_))
        {
            return;
        }

        string[] lines = value_.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).TakeLast(8).ToArray();
        foreach (string line in lines)
        {
            builder_.AppendLine($"  - {label_}: `{line.Replace("`", "'", StringComparison.Ordinal)}`");
        }
    }

    private sealed record ScriptPlan(ScriptDefinition Definition);
}
