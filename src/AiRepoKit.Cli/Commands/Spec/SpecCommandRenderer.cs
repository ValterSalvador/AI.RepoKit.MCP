using System.Globalization;
using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using AiRepoKit.Spec.Projection;

namespace AiRepoKit.Cli.Commands.Spec;

public static class SpecCommandRenderer
{
    public static string GetUsage()
    {
        return """
        # Spec Command

        Usage:

        ```text
        airepo spec [help]
        airepo spec init --spec-id <spec-id> --from <requirements.json> [--repo <path>] [--dry-run | --apply] [--json]
        airepo spec show --spec-id <spec-id> [--repo <path>] [--artifact requirements|work-spec|approvals|all] [--json]
        airepo spec refine --spec-id <spec-id> --artifact requirements|work-spec --from <candidate.json> [--expected-revision <n>] [--repo <path>] [--dry-run | --apply] [--json]
        airepo spec approve --spec-id <spec-id> --artifact requirements|work-spec --revision <n> [--repo <path>] [--dry-run | --apply] [--json]
        ```

        Lifecycle subcommands:

        - `init`: Initializes a new RequirementSet artifact at revision 1 (dry-run by default).
        - `show`: Displays canonical lifecycle state and derived approval statuses (read-only).
        - `refine`: Refines an existing RequirementSet or initial/existing WorkSpec.
        - `approve`: Records an approval for a RequirementSet or WorkSpec in the approval ledger.
        """;
    }

    public static CommandResult RenderError(
        string message,
        bool isJson,
        string? errorCode = null,
        IReadOnlyList<string>? validationErrors = null,
        bool includeUsage = false)
    {
        if (isJson)
        {
            SpecCliErrorDto dto = new()
            {
                Error = message,
                ErrorCode = errorCode,
                ValidationErrors = validationErrors
            };
            return CommandResult.Failure(SpecJsonSerializer.Serialize(dto), 1);
        }

        StringBuilder builder = new();
        builder.AppendLine(string.IsNullOrWhiteSpace(errorCode)
            ? "# Spec Command Error"
            : $"# Spec Persistence Error: {errorCode}");
        builder.AppendLine();
        builder.AppendLine(message);

        if (validationErrors is not null && validationErrors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Validation Errors");
            foreach (string err in validationErrors)
            {
                builder.AppendLine($"- {err}");
            }
        }

        if (includeUsage)
        {
            builder.AppendLine();
            builder.AppendLine(GetUsage());
        }

        return CommandResult.Failure(builder.ToString().TrimEnd(), 1);
    }

    public static CommandResult RenderPersistenceError(SpecPersistenceException exception, bool isJson)
    {
        List<string>? validationErrors = exception.ValidationErrors.Count > 0
            ? exception.ValidationErrors.Select(e => $"[{e.Code}] {e.Message}").ToList()
            : null;

        return RenderError(exception.Message, isJson, exception.ErrorCode, validationErrors);
    }

    public static CommandResult RenderInitResult(SpecId specId, SpecStoreResult result, bool isJson)
    {
        if (isJson)
        {
            SpecMutationResultDto dto = new()
            {
                SpecId = specId.Value,
                ArtifactKind = result.ArtifactKind,
                Mode = result.Mode,
                Changed = result.Changed,
                Applied = result.Applied,
                PreviousRevision = result.PreviousRevision,
                TargetRevision = result.TargetRevision,
                SemanticDigest = result.SemanticDigest
            };
            return CommandResult.Ok(SpecJsonSerializer.Serialize(dto));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Init: `{specId.Value}`");
        builder.AppendLine();
        builder.AppendLine($"- Spec ID: `{specId.Value}`");
        builder.AppendLine($"- Artifact: `{result.ArtifactKind}`");
        builder.AppendLine($"- Mode: `{result.Mode}`");
        builder.AppendLine($"- Changed: `{result.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Applied: `{result.Applied.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Target Revision: `{result.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Digest: `{result.SemanticDigest}`");
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderRefineResult(SpecId specId, SpecStoreResult result, bool isJson)
    {
        if (isJson)
        {
            SpecMutationResultDto dto = new()
            {
                SpecId = specId.Value,
                ArtifactKind = result.ArtifactKind,
                Mode = result.Mode,
                Changed = result.Changed,
                Applied = result.Applied,
                PreviousRevision = result.PreviousRevision,
                TargetRevision = result.TargetRevision,
                SemanticDigest = result.SemanticDigest
            };
            return CommandResult.Ok(SpecJsonSerializer.Serialize(dto));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Refine: `{specId.Value}`");
        builder.AppendLine();
        builder.AppendLine($"- Spec ID: `{specId.Value}`");
        builder.AppendLine($"- Artifact: `{result.ArtifactKind}`");
        builder.AppendLine($"- Mode: `{result.Mode}`");
        builder.AppendLine($"- Changed: `{result.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Applied: `{result.Applied.ToString().ToLowerInvariant()}`");
        if (result.PreviousRevision is not null)
        {
            builder.AppendLine($"- Previous Revision: `{result.PreviousRevision.Value.Value.ToString(CultureInfo.InvariantCulture)}`");
        }
        builder.AppendLine($"- Target Revision: `{result.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Digest: `{result.SemanticDigest}`");
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderApproveResult(
        SpecId specId,
        SpecApprovalLedgerStoreResult result,
        SpecApprovalStatus currentApprovalStatus,
        SpecApprovalStatus proposedApprovalStatus,
        bool isJson)
    {
        if (isJson)
        {
            SpecApprovalResultDto dto = new()
            {
                SpecId = specId.Value,
                ArtifactKind = result.Approval.ArtifactKind,
                Mode = result.Mode,
                Changed = result.Changed,
                Applied = result.Applied,
                TargetRevision = result.Approval.ArtifactRevision,
                SemanticDigest = result.Approval.SemanticDigest,
                ApprovalId = result.Approval.Id.Value,
                CurrentApprovalStatus = currentApprovalStatus,
                ProposedApprovalStatus = proposedApprovalStatus,
                LedgerPreviousRevision = result.PreviousRevision,
                LedgerTargetRevision = result.TargetRevision
            };
            return CommandResult.Ok(SpecJsonSerializer.Serialize(dto));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Approve: `{specId.Value}`");
        builder.AppendLine();
        builder.AppendLine($"- Spec ID: `{specId.Value}`");
        builder.AppendLine($"- Artifact: `{result.Approval.ArtifactKind}`");
        builder.AppendLine($"- Mode: `{result.Mode}`");
        builder.AppendLine($"- Changed: `{result.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Applied: `{result.Applied.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Target Revision: `{result.Approval.ArtifactRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Approval ID: `{result.Approval.Id.Value}`");
        builder.AppendLine($"- Current Approval Status: `{currentApprovalStatus}`");
        builder.AppendLine($"- Proposed Approval Status: `{proposedApprovalStatus}`");
        builder.AppendLine($"- Ledger Previous Revision: `{(result.PreviousRevision is not null ? result.PreviousRevision.Value.Value.ToString(CultureInfo.InvariantCulture) : "none")}`");
        builder.AppendLine($"- Ledger Target Revision: `{result.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Digest: `{result.Approval.SemanticDigest}`");
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderShowResult(
        SpecId specId,
        string artifactSelector,
        SpecWorkspaceSnapshot snapshot,
        SpecApprovalLedger? ledger,
        IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses,
        bool isJson)
    {
        SpecArtifactApprovalStatus? reqStatus = approvalStatuses.FirstOrDefault(s => s.ArtifactKind == SpecArtifactKind.RequirementSet);
        SpecArtifactApprovalStatus? wsStatus = approvalStatuses.FirstOrDefault(s => s.ArtifactKind == SpecArtifactKind.WorkSpec);
        SpecArtifactApprovalStatus? planStatus = approvalStatuses.FirstOrDefault(s => s.ArtifactKind == SpecArtifactKind.ImplementationPlan);

        if (isJson)
        {
            return artifactSelector switch
            {
                "requirements" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowRequirementsDto
                {
                    SpecId = specId.Value,
                    Present = snapshot.RequirementSet is not null,
                    Revision = snapshot.RequirementSet?.Revision,
                    SemanticDigest = snapshot.RequirementSet is null ? null : SpecSemanticDigest.Compute(snapshot.RequirementSet),
                    ApprovalStatus = reqStatus?.Status,
                    Content = snapshot.RequirementSet
                })),
                "work-spec" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowWorkSpecDto
                {
                    SpecId = specId.Value,
                    Present = snapshot.WorkSpec is not null,
                    Stale = snapshot.WorkSpec is null ? null : snapshot.IsWorkSpecStale,
                    Revision = snapshot.WorkSpec?.Revision,
                    SemanticDigest = snapshot.WorkSpec is null ? null : SpecSemanticDigest.Compute(snapshot.WorkSpec),
                    ApprovalStatus = wsStatus?.Status,
                    Content = snapshot.WorkSpec
                })),
                "approvals" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowApprovalsDto
                {
                    SpecId = specId.Value,
                    Present = ledger is not null,
                    Revision = ledger?.Revision,
                    ApprovalCount = ledger?.Approvals.Count,
                    Content = ledger
                })),
                _ => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowAllDto
                {
                    SpecId = specId.Value,
                    ApprovalStatuses = approvalStatuses,
                    Requirements = new SpecShowRequirementsDto
                    {
                        SpecId = specId.Value,
                        Present = snapshot.RequirementSet is not null,
                        Revision = snapshot.RequirementSet?.Revision,
                        SemanticDigest = snapshot.RequirementSet is null ? null : SpecSemanticDigest.Compute(snapshot.RequirementSet),
                        ApprovalStatus = reqStatus?.Status,
                        Content = snapshot.RequirementSet
                    },
                    WorkSpec = new SpecShowWorkSpecDto
                    {
                        SpecId = specId.Value,
                        Present = snapshot.WorkSpec is not null,
                        Stale = snapshot.WorkSpec is null ? null : snapshot.IsWorkSpecStale,
                        Revision = snapshot.WorkSpec?.Revision,
                        SemanticDigest = snapshot.WorkSpec is null ? null : SpecSemanticDigest.Compute(snapshot.WorkSpec),
                        ApprovalStatus = wsStatus?.Status,
                        Content = snapshot.WorkSpec
                    },
                    ImplementationPlan = snapshot.ImplementationPlan is null ? null : new SpecShowImplementationPlanDto
                    {
                        Present = true,
                        Stale = snapshot.IsImplementationPlanStale,
                        Revision = snapshot.ImplementationPlan.Revision,
                        SemanticDigest = SpecSemanticDigest.Compute(snapshot.ImplementationPlan),
                        ApprovalStatus = planStatus?.Status,
                        Content = snapshot.ImplementationPlan
                    },
                    Approvals = new SpecShowApprovalsDto
                    {
                        SpecId = specId.Value,
                        Present = ledger is not null,
                        Revision = ledger?.Revision,
                        ApprovalCount = ledger?.Approvals.Count,
                        Content = ledger
                    }
                }))
            };
        }

        StringBuilder builder = new();
        switch (artifactSelector)
        {
            case "requirements":
                builder.AppendLine($"# Spec Requirements: `{specId.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(snapshot.RequirementSet is not null).ToString().ToLowerInvariant()}`");
                if (snapshot.RequirementSet is not null)
                {
                    builder.AppendLine($"- Revision: `{snapshot.RequirementSet.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Approval Status: `{reqStatus?.Status.ToString() ?? "NotApproved"}`");
                    builder.AppendLine($"- Semantic Digest: `{SpecSemanticDigest.Compute(snapshot.RequirementSet)}`");
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot.RequirementSet));
                }
                break;

            case "work-spec":
                builder.AppendLine($"# Spec Work Spec: `{specId.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(snapshot.WorkSpec is not null).ToString().ToLowerInvariant()}`");
                if (snapshot.WorkSpec is not null)
                {
                    builder.AppendLine($"- Revision: `{snapshot.WorkSpec.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Stale: `{snapshot.IsWorkSpecStale.ToString().ToLowerInvariant()}`");
                    builder.AppendLine($"- Approval Status: `{wsStatus?.Status.ToString() ?? "NotApproved"}`");
                    builder.AppendLine($"- Semantic Digest: `{SpecSemanticDigest.Compute(snapshot.WorkSpec)}`");
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot.WorkSpec));
                }
                break;

            case "approvals":
                builder.AppendLine($"# Spec Approvals: `{specId.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(ledger is not null).ToString().ToLowerInvariant()}`");
                if (ledger is not null)
                {
                    builder.AppendLine($"- Ledger Revision: `{ledger.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Approvals Count: `{ledger.Approvals.Count.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine();
                    builder.AppendLine("## Ledger Approvals");
                    builder.AppendLine();
                    if (ledger.Approvals.Count == 0)
                    {
                        builder.AppendLine("_None._");
                    }
                    else
                    {
                        foreach (Approval approval in ledger.Approvals)
                        {
                            builder.AppendLine($"- `{approval.Id.Value}`: `{approval.ArtifactKind}` rev `{approval.ArtifactRevision.Value.ToString(CultureInfo.InvariantCulture)}` (Digest: `{approval.SemanticDigest}`)");
                        }
                    }
                }
                break;

            default:
                builder.AppendLine($"# Spec: `{specId.Value}`");
                builder.AppendLine();
                builder.AppendLine("## Lifecycle State");
                builder.AppendLine();
                builder.AppendLine($"- Requirements: {(snapshot.RequirementSet is not null ? $"Present (rev {snapshot.RequirementSet.Revision.Value.ToString(CultureInfo.InvariantCulture)}, status: {reqStatus?.Status.ToString() ?? "NotApproved"})" : "Not present")}");
                builder.AppendLine($"- Work Spec: {(snapshot.WorkSpec is not null ? $"Present (rev {snapshot.WorkSpec.Revision.Value.ToString(CultureInfo.InvariantCulture)}, status: {wsStatus?.Status.ToString() ?? "NotApproved"}, stale: {snapshot.IsWorkSpecStale.ToString().ToLowerInvariant()})" : "Not present")}");
                builder.AppendLine($"- Implementation Plan: {(snapshot.ImplementationPlan is not null ? $"Present (rev {snapshot.ImplementationPlan.Revision.Value.ToString(CultureInfo.InvariantCulture)}, status: {planStatus?.Status.ToString() ?? "NotApproved"}, stale: {snapshot.IsImplementationPlanStale.ToString().ToLowerInvariant()})" : "Not present")}");
                builder.AppendLine($"- Approval Ledger: {(ledger is not null ? $"Present (rev {ledger.Revision.Value.ToString(CultureInfo.InvariantCulture)}, {ledger.Approvals.Count.ToString(CultureInfo.InvariantCulture)} approvals)" : "Not present")}");

                if (snapshot.RequirementSet is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot.RequirementSet));
                }

                if (snapshot.WorkSpec is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot.WorkSpec));
                }

                if (snapshot.ImplementationPlan is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot.ImplementationPlan));
                }

                if (ledger is not null && ledger.Approvals.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.AppendLine("## Ledger Approvals");
                    builder.AppendLine();
                    foreach (Approval approval in ledger.Approvals)
                    {
                        builder.AppendLine($"- `{approval.Id.Value}`: `{approval.ArtifactKind}` rev `{approval.ArtifactRevision.Value.ToString(CultureInfo.InvariantCulture)}` (Digest: `{approval.SemanticDigest}`)");
                    }
                }
                break;
        }

        return CommandResult.Ok(builder.ToString().TrimEnd());
    }
}
