using AiRepoKit.Cli.Commands.Spec;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Cli.Services.SpecVerification;
using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Diff;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Projection;
using AiRepoKit.Spec.Verification;

namespace AiRepoKit.Cli.Commands;

public sealed class SpecCommand
{
    public CommandResult Execute(IReadOnlyList<string> arguments_)
    {
        if (arguments_.Count == 0)
        {
            return CommandResult.Ok(SpecCommandRenderer.GetUsage());
        }

        string subcommand = arguments_[0];
        bool isHelp = string.Equals(subcommand, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcommand, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcommand, "-h", StringComparison.OrdinalIgnoreCase);

        if (isHelp)
        {
            if (arguments_.Count == 1)
            {
                return CommandResult.Ok(SpecCommandRenderer.GetUsage());
            }

            return CommandResult.Failure(
                $"# Spec Command Error{Environment.NewLine}{Environment.NewLine}Unexpected argument(s) after Spec help: `{string.Join("`, `", arguments_.Skip(1))}`.{Environment.NewLine}{Environment.NewLine}{SpecCommandRenderer.GetUsage()}",
                1);
        }

        return subcommand.ToLowerInvariant() switch
        {
            "init" => this.ExecuteInit(arguments_.Skip(1).ToArray()),
            "show" => this.ExecuteShow(arguments_.Skip(1).ToArray()),
            "refine" => this.ExecuteRefine(arguments_.Skip(1).ToArray()),
            "plan" => this.ExecutePlan(arguments_.Skip(1).ToArray()),
            "checklist" => this.ExecuteChecklist(arguments_.Skip(1).ToArray()),
            "approve" => this.ExecuteApprove(arguments_.Skip(1).ToArray()),
            "diff" => this.ExecuteDiff(arguments_.Skip(1).ToArray()),
            "verify" => this.ExecuteVerify(arguments_.Skip(1).ToArray()),
            _ => this.HandleUnknownSubcommand(subcommand, arguments_)
        };

    }

    private CommandResult HandleUnknownSubcommand(string subcommand_, IReadOnlyList<string> arguments_)
    {
        bool isJson = arguments_.Any(arg_ => string.Equals(arg_, "--json", StringComparison.OrdinalIgnoreCase));
        string error = $"Unsupported Spec subcommand: `{subcommand_}`.";
        return SpecCommandRenderer.RenderError(error, isJson, includeUsage_: true);
    }

    private CommandResult ExecuteInit(IReadOnlyList<string> args_)
    {
        SpecInitOptions options;
        try
        {
            options = SpecCommandParser.ParseInit(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        RequirementSet candidate;
        try
        {
            candidate = SpecCommandInputReader.ReadCandidate<RequirementSet>(options.FromPath, SpecArtifactKind.RequirementSet);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Failed to read candidate artifact: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecStoreResult result = service.InitializeRequirementSet(
                candidate,
                new SpecStoreOptions
                {
                    Mode = options.Mode
                });

            return SpecCommandRenderer.RenderInitResult(options.SpecId, result, options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec init failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecuteShow(IReadOnlyList<string> args_)
    {
        SpecShowOptions options;
        try
        {
            options = SpecCommandParser.ParseShow(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
            SpecApprovalLedger? ledger = service.LedgerStore.Load();
            IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses = service.GetApprovalStatuses();

            return SpecCommandRenderer.RenderShowResult(
                options.SpecId,
                options.ArtifactSelector,
                snapshot,
                ledger,
                approvalStatuses,
                options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec show failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecuteRefine(IReadOnlyList<string> args_)
    {
        SpecRefineOptions options;
        try
        {
            options = SpecCommandParser.ParseRefine(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
            SpecStoreResult result;

            if (options.Artifact == "requirements")
            {
                if (options.ExpectedRevision is null)
                {
                    return SpecCommandRenderer.RenderError("Refining RequirementSet requires '--expected-revision'.", options.IsJson);
                }

                if (snapshot.RequirementSet is not null && snapshot.RequirementSet.Revision != options.ExpectedRevision.Value)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.RevisionConflict,
                        $"The expected current revision '{options.ExpectedRevision.Value.Value}' does not match the canonical RequirementSet revision '{snapshot.RequirementSet.Revision.Value}'.",
                        SpecArtifactKind.RequirementSet);
                }

                RequirementSet candidate = SpecCommandInputReader.ReadCandidate<RequirementSet>(options.FromPath, SpecArtifactKind.RequirementSet);
                result = service.RefineRequirementSet(
                    candidate,
                    new SpecStoreOptions
                    {
                        Mode = options.Mode,
                        ExpectedCurrentRevision = options.ExpectedRevision
                    });
            }
            else
            {
                bool workSpecExists = snapshot.WorkSpec is not null;
                if (!workSpecExists)
                {
                    if (options.ExpectedRevision is not null)
                    {
                        return SpecCommandRenderer.RenderError("Cannot specify '--expected-revision' when creating the initial WorkSpec.", options.IsJson);
                    }
                }
                else
                {
                    if (options.ExpectedRevision is null)
                    {
                        return SpecCommandRenderer.RenderError("Refining existing WorkSpec requires '--expected-revision'.", options.IsJson);
                    }

                    if (snapshot.WorkSpec!.Revision != options.ExpectedRevision.Value)
                    {
                        throw new SpecPersistenceException(
                            SpecPersistenceException.RevisionConflict,
                            $"The expected current revision '{options.ExpectedRevision.Value.Value}' does not match the canonical WorkSpec revision '{snapshot.WorkSpec!.Revision.Value}'.",
                            SpecArtifactKind.WorkSpec);
                    }
                }

                WorkSpec candidate = SpecCommandInputReader.ReadCandidate<WorkSpec>(options.FromPath, SpecArtifactKind.WorkSpec);
                result = service.RefineWorkSpec(
                    candidate,
                    new SpecStoreOptions
                    {
                        Mode = options.Mode,
                        ExpectedCurrentRevision = options.ExpectedRevision
                    });
            }

            return SpecCommandRenderer.RenderRefineResult(options.SpecId, result, options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec refine failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecutePlan(IReadOnlyList<string> args_)
    {
        SpecPlanOptions options;
        try
        {
            options =
                SpecCommandParser.ParsePlan(
                    args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(
                exception.Message,
                exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot =
                ResolveRepo(
                    options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Repository path resolution failed: " +
                exception.Message,
                options.IsJson);
        }

        try
        {
            ImplementationPlan candidate =
                SpecCommandInputReader.ReadCandidate<ImplementationPlan>(
                    options.FromPath,
                    SpecArtifactKind.ImplementationPlan);

            SpecLifecycleService service =
                new(
                    repoRoot,
                    options.SpecId);

            SpecStoreResult result =
                service.RefineImplementationPlan(
                    candidate,
                    new SpecStoreOptions
                    {
                        Mode =
                            options.Mode,
                        ExpectedCurrentRevision =
                            options.ExpectedRevision
                    });

            return SpecCommandRenderer.RenderPlanResult(
                options.SpecId,
                result,
                options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(
                exception,
                options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Spec plan failed: " +
                exception.Message,
                options.IsJson);
        }
    }
    private CommandResult ExecuteChecklist(
        IReadOnlyList<string> args_)
    {
        SpecChecklistOptions options;

        try
        {
            options =
                SpecCommandParser.ParseChecklist(
                    args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(
                exception.Message,
                exception.IsJson);
        }

        string repoRoot;

        try
        {
            repoRoot =
                ResolveRepo(
                    options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Repository path resolution failed: " +
                exception.Message,
                options.IsJson);
        }

        try
        {
            SpecLifecycleService service =
                new(
                    repoRoot,
                    options.SpecId);

            SpecWorkspaceSnapshot snapshot =
                service.Workspace.Load();

            SpecApprovalLedger? ledger =
                service.LedgerStore.Load();

            ImplementationChecklistProjection projection =
                ImplementationChecklistProjector.Project(
                    options.SpecId,
                    snapshot,
                    ledger);

            string output =
                options.IsJson
                    ? ImplementationChecklistProjector.ProjectJson(
                        projection)
                    : ImplementationChecklistProjector.ProjectMarkdown(
                        projection);

            return CommandResult.Ok(
                output);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(
                exception,
                options.IsJson);
        }
        catch (InvalidOperationException exception)
        {
            return SpecCommandRenderer.RenderError(
                exception.Message,
                options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Spec checklist failed: " +
                exception.Message,
                options.IsJson);
        }
    }
    private CommandResult ExecuteApprove(IReadOnlyList<string> args_)
    {
        SpecApproveOptions options;
        try
        {
            options = SpecCommandParser.ParseApprove(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);

            SpecArtifactKind targetKind =
                options.Artifact switch
                {
                    "requirements" =>
                        SpecArtifactKind.RequirementSet,
                    "work-spec" =>
                        SpecArtifactKind.WorkSpec,
                    "implementation-plan" =>
                        SpecArtifactKind.ImplementationPlan,
                    _ =>
                        throw new InvalidOperationException(
                            $"Unsupported approval artifact '{options.Artifact}'.")
                };

            SpecArtifactApprovalStatus? preStatus = service.GetApprovalStatus(targetKind);
            SpecApprovalStatus currentPersistedStatus = preStatus?.Status ?? SpecApprovalStatus.NotApproved;

            SpecApprovalLedger? currentLedger = service.LedgerStore.Load();
            SpecStoreOptions ledgerOptions = new()
            {
                Mode = options.Mode,
                ExpectedCurrentRevision = currentLedger?.Revision
            };

            SpecApprovalLedgerStoreResult result =
                targetKind switch
                {
                    SpecArtifactKind.RequirementSet =>
                        service.ApproveRequirementSet(
                            options.Revision,
                            ledgerOptions),
                    SpecArtifactKind.WorkSpec =>
                        service.ApproveWorkSpec(
                            options.Revision,
                            ledgerOptions),
                    SpecArtifactKind.ImplementationPlan =>
                        service.ApproveImplementationPlan(
                            options.Revision,
                            ledgerOptions),
                    _ =>
                        throw new InvalidOperationException(
                            $"Unsupported approval artifact '{targetKind}'.")
                };

            SpecApprovalStatus currentApprovalStatus = options.Mode == SpecWriteMode.Apply
                ? SpecApprovalStatus.Current
                : currentPersistedStatus;
            SpecApprovalStatus proposedApprovalStatus = SpecApprovalStatus.Current;

            return SpecCommandRenderer.RenderApproveResult(
                options.SpecId,
                result,
                currentApprovalStatus,
                proposedApprovalStatus,
                options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec approve failed: " + exception.Message, options.IsJson);
        }
    }

    private CommandResult ExecuteDiff(IReadOnlyList<string> args_)
    {
        SpecDiffOptions options;
        try
        {
            options = SpecCommandParser.ParseDiff(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Repository path resolution failed: " + exception.Message, options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
            SpecApprovalLedger? ledger = service.LedgerStore.Load();

            SpecDiffResult diffResult = options.Artifact switch
            {
                "requirements" => ExecuteDiffRequirements(options, snapshot, ledger),
                "work-spec" => ExecuteDiffWorkSpec(options, snapshot, ledger),
                "implementation-plan" => ExecuteDiffImplementationPlan(options, snapshot, ledger),
                _ => throw new InvalidOperationException($"Unsupported artifact selector '{options.Artifact}'.")
            };

            return SpecCommandRenderer.RenderDiffResult(diffResult, options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError("Spec diff failed: " + exception.Message, options.IsJson);
        }
    }

    private static SpecDiffResult ExecuteDiffRequirements(
        SpecDiffOptions options_,
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_)
    {
        RequirementSet candidate = SpecCommandInputReader.ReadCandidate<RequirementSet>(
            options_.FromPath,
            SpecArtifactKind.RequirementSet);

        return SpecDiffAnalyzer.Analyze(
            options_.SpecId,
            snapshot_,
            ledger_,
            SpecArtifactKind.RequirementSet,
            candidate);
    }

    private static SpecDiffResult ExecuteDiffWorkSpec(
        SpecDiffOptions options_,
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_)
    {
        WorkSpec candidate = SpecCommandInputReader.ReadCandidate<WorkSpec>(
            options_.FromPath,
            SpecArtifactKind.WorkSpec);

        return SpecDiffAnalyzer.Analyze(
            options_.SpecId,
            snapshot_,
            ledger_,
            SpecArtifactKind.WorkSpec,
            candidate);
    }

    private static SpecDiffResult ExecuteDiffImplementationPlan(
        SpecDiffOptions options_,
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_)
    {
        ImplementationPlan candidate = SpecCommandInputReader.ReadCandidate<ImplementationPlan>(
            options_.FromPath,
            SpecArtifactKind.ImplementationPlan);

        return SpecDiffAnalyzer.Analyze(
            options_.SpecId,
            snapshot_,
            ledger_,
            SpecArtifactKind.ImplementationPlan,
            candidate);
    }


    private static string ResolveRepo(string? repoPathRaw_)
    {
        return new RepoPathResolver().Resolve(repoPathRaw_, "spec");
    }

    private CommandResult ExecuteVerify(IReadOnlyList<string> args_)
    {
        SpecVerifyOptions options;
        try
        {
            options = SpecCommandParser.ParseVerify(args_);
        }
        catch (SpecCliParsingException exception)
        {
            return SpecCommandRenderer.RenderError(exception.Message, exception.IsJson);
        }

        string repoRoot;
        try
        {
            repoRoot = ResolveRepo(options.RepoPath);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Repository path resolution failed: " + exception.Message,
                options.IsJson);
        }

        SpecVerificationRequest request;
        try
        {
            request = SpecCommandInputReader.ReadBoundedJson<SpecVerificationRequest>(options.FromPath);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Failed to read verification request: " + exception.Message,
                options.IsJson);
        }

        try
        {
            SpecLifecycleService service = new(repoRoot, options.SpecId);
            SpecWorkspaceSnapshot snapshot = service.Workspace.Load();
            SpecApprovalLedger? ledger = service.LedgerStore.Load();

            // Prerequisite: canonical graph must exist
            if (snapshot.RequirementSet is null)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical RequirementSet does not exist. Run 'spec init' first.",
                    options.IsJson);
            }

            if (snapshot.WorkSpec is null)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical WorkSpec does not exist. Run 'spec refine' first.",
                    options.IsJson);
            }

            if (snapshot.ImplementationPlan is null)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical ImplementationPlan does not exist. Run 'spec plan' first.",
                    options.IsJson);
            }

            if (snapshot.IsWorkSpecStale)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical WorkSpec is stale relative to RequirementSet. Re-approve or update the WorkSpec.",
                    options.IsJson);
            }

            if (snapshot.IsImplementationPlanStale)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical ImplementationPlan is stale relative to WorkSpec. Re-approve or update the ImplementationPlan.",
                    options.IsJson);
            }

            IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses =
                SpecApprovalStatusEvaluator.Evaluate(snapshot, ledger);

            SpecArtifactApprovalStatus? reqStatus =
                approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.RequirementSet);
            if (reqStatus is null || reqStatus.Status != SpecApprovalStatus.Current)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical RequirementSet approval status is not Current. Approve the RequirementSet first.",
                    options.IsJson);
            }

            SpecArtifactApprovalStatus? wsStatus =
                approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.WorkSpec);
            if (wsStatus is null || wsStatus.Status != SpecApprovalStatus.Current)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical WorkSpec approval status is not Current. Approve the WorkSpec first.",
                    options.IsJson);
            }

            SpecArtifactApprovalStatus? planStatus =
                approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan);
            if (planStatus is null || planStatus.Status != SpecApprovalStatus.Current)
            {
                return SpecCommandRenderer.RenderError(
                    "Canonical ImplementationPlan approval status is not Current. Approve the ImplementationPlan first.",
                    options.IsJson);
            }

            // Validate traceability using existing VerificationValidator
            IReadOnlyList<VerificationEvidence> evidenceList =
                request.Evidence.Select(b_ => b_.Evidence).ToArray();
            IReadOnlyList<SpecValidationError> traceErrors =
                VerificationValidator.Validate(
                    evidenceList,
                    [],
                    snapshot.WorkSpec,
                    snapshot.ImplementationPlan);

            if (traceErrors.Count > 0)
            {
                IReadOnlyList<string> errorMessages =
                    traceErrors.Select(e_ => $"[{e_.Code}] {e_.Message}").ToArray();
                return SpecCommandRenderer.RenderError(
                    "Verification request has invalid traceability.",
                    options.IsJson,
                    errorCode_: SpecPersistenceException.ValidationFailed,
                    validationErrors_: errorMessages);
            }

            // Collect repository evidence
            RepositoryEvidenceCollector collector = new();
            RepositoryEvidenceCollection collection = collector.Collect(
                new RepositoryEvidenceCollectionRequest(repoRoot, "spec-verify", 10, 500));

            // Build lookup keyed by EvidenceId
            Dictionary<string, RepositoryEvidence> evidenceLookup =
                collection.Evidence.ToDictionary(e_ => e_.EvidenceId, StringComparer.Ordinal);

            // Bind observations
            SpecVerificationEvidenceBinder binder = new();
            IReadOnlyList<SpecVerificationEvidenceObservation> observations =
                binder.Bind(request.Evidence, evidenceLookup, repoRoot, out IReadOnlyList<string> bindErrors);

            if (bindErrors.Count > 0)
            {
                return SpecCommandRenderer.RenderError(
                    "Verification request has invalid evidence bindings.",
                    options.IsJson,
                    errorCode_: SpecPersistenceException.ValidationFailed,
                    validationErrors_: bindErrors);
            }

            // Evaluate
            SpecVerificationReport report = SpecVerificationEvaluator.Evaluate(
                options.SpecId.Value,
                snapshot.RequirementSet.Revision,
                snapshot.WorkSpec.Revision,
                snapshot.ImplementationPlan.Revision,
                evidenceList,
                observations,
                snapshot.WorkSpec);

            return SpecCommandRenderer.RenderVerifyResult(options.SpecId, report, options.IsJson);
        }
        catch (SpecPersistenceException exception)
        {
            return SpecCommandRenderer.RenderPersistenceError(exception, options.IsJson);
        }
        catch (Exception exception)
        {
            return SpecCommandRenderer.RenderError(
                "Spec verify failed: " + exception.Message,
                options.IsJson);
        }
    }
}
