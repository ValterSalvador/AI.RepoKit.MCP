using System.Globalization;
using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Diff;
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
        airepo spec show --spec-id <spec-id> [--repo <path>] [--artifact requirements|work-spec|implementation-plan|approvals|all] [--json]
        airepo spec refine --spec-id <spec-id> --artifact requirements|work-spec --from <candidate.json> [--expected-revision <n>] [--repo <path>] [--dry-run | --apply] [--json]
        airepo spec plan --spec-id <spec-id> --from <candidate.json> [--expected-revision <n>] [--repo <path>] [--dry-run | --apply] [--json]
        airepo spec approve --spec-id <spec-id> --artifact requirements|work-spec|implementation-plan --revision <n> [--repo <path>] [--dry-run | --apply] [--json]
        airepo spec checklist --spec-id <spec-id> [--repo <path>] [--json]
        airepo spec diff --spec-id <spec-id> --artifact requirements|work-spec|implementation-plan --from <candidate.json> [--repo <path>] [--json]
        ```

        Lifecycle subcommands:

        - `init`: Initializes a new RequirementSet artifact at revision 1 (dry-run by default).
        - `show`: Displays canonical lifecycle state and derived approval statuses (read-only).
        - `refine`: Refines an existing RequirementSet or initial/existing WorkSpec.
        - `plan`: Creates or refines the canonical ImplementationPlan (dry-run by default).
        - `approve`: Records an approval for a RequirementSet, WorkSpec, or ImplementationPlan in the approval ledger.
        - `checklist`: Displays the derived implementation checklist projected from the canonical ImplementationPlan (read-only).
        - `diff`: Semantically compares a candidate artifact against canonical state and analyzes downstream invalidation (read-only).
        """;
    }

    public static CommandResult RenderError(
        string message_,
        bool isJson_,
        string? errorCode_ = null,
        IReadOnlyList<string>? validationErrors_ = null,
        bool includeUsage_ = false)
    {
        if (isJson_)
        {
            SpecCliErrorDto dto = new()
            {
                Error = message_,
                ErrorCode = errorCode_,
                ValidationErrors = validationErrors_
            };
            return CommandResult.Failure(SpecJsonSerializer.Serialize(dto), 1);
        }

        StringBuilder builder = new();
        builder.AppendLine(string.IsNullOrWhiteSpace(errorCode_)
            ? "# Spec Command Error"
            : $"# Spec Persistence Error: {errorCode_}");
        builder.AppendLine();
        builder.AppendLine(message_);

        if (validationErrors_ is not null && validationErrors_.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Validation Errors");
            foreach (string err in validationErrors_)
            {
                builder.AppendLine($"- {err}");
            }
        }

        if (includeUsage_)
        {
            builder.AppendLine();
            builder.AppendLine(GetUsage());
        }

        return CommandResult.Failure(builder.ToString().TrimEnd(), 1);
    }

    public static CommandResult RenderPersistenceError(SpecPersistenceException exception_, bool isJson_)
    {
        List<string>? validationErrors = exception_.ValidationErrors.Count > 0
            ? exception_.ValidationErrors.Select(e_ => $"[{e_.Code}] {e_.Message}").ToList()
            : null;

        return RenderError(exception_.Message, isJson_, exception_.ErrorCode, validationErrors);
    }

    public static CommandResult RenderInitResult(SpecId specId_, SpecStoreResult result_, bool isJson_)
    {
        if (isJson_)
        {
            SpecMutationResultDto dto = new()
            {
                SpecId = specId_.Value,
                ArtifactKind = result_.ArtifactKind,
                Mode = result_.Mode,
                Changed = result_.Changed,
                Applied = result_.Applied,
                PreviousRevision = result_.PreviousRevision,
                TargetRevision = result_.TargetRevision,
                CurrentRevision = result_.Applied ? result_.TargetRevision : result_.PreviousRevision,
                SemanticDigest = result_.SemanticDigest
            };
            return CommandResult.Ok(SpecJsonSerializer.Serialize(dto));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Init: `{specId_.Value}`");
        builder.AppendLine();
        builder.AppendLine($"- Spec ID: `{specId_.Value}`");
        builder.AppendLine($"- Artifact: `{result_.ArtifactKind}`");
        builder.AppendLine($"- Mode: `{result_.Mode}`");
        builder.AppendLine($"- Changed: `{result_.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Applied: `{result_.Applied.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Target Revision: `{result_.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Digest: `{result_.SemanticDigest}`");
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderRefineResult(SpecId specId_, SpecStoreResult result_, bool isJson_)
    {
        if (isJson_)
        {
            SpecMutationResultDto dto = new()
            {
                SpecId = specId_.Value,
                ArtifactKind = result_.ArtifactKind,
                Mode = result_.Mode,
                Changed = result_.Changed,
                Applied = result_.Applied,
                PreviousRevision = result_.PreviousRevision,
                TargetRevision = result_.TargetRevision,
                CurrentRevision = result_.Applied ? result_.TargetRevision : result_.PreviousRevision,
                SemanticDigest = result_.SemanticDigest
            };
            return CommandResult.Ok(SpecJsonSerializer.Serialize(dto));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Refine: `{specId_.Value}`");
        builder.AppendLine();
        builder.AppendLine($"- Spec ID: `{specId_.Value}`");
        builder.AppendLine($"- Artifact: `{result_.ArtifactKind}`");
        builder.AppendLine($"- Mode: `{result_.Mode}`");
        builder.AppendLine($"- Changed: `{result_.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Applied: `{result_.Applied.ToString().ToLowerInvariant()}`");
        if (result_.PreviousRevision is not null)
        {
            builder.AppendLine($"- Previous Revision: `{result_.PreviousRevision.Value.Value.ToString(CultureInfo.InvariantCulture)}`");
        }
        builder.AppendLine($"- Target Revision: `{result_.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Digest: `{result_.SemanticDigest}`");
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderPlanResult(
        SpecId specId_,
        SpecStoreResult result_,
        bool isJson_)
    {
        if (isJson_)
        {
            SpecMutationResultDto dto =
                new()
                {
                    SpecId =
                        specId_.Value,
                    ArtifactKind =
                        result_.ArtifactKind,
                    Mode =
                        result_.Mode,
                    Changed =
                        result_.Changed,
                    Applied =
                        result_.Applied,
                    PreviousRevision =
                        result_.PreviousRevision,
                    TargetRevision =
                        result_.TargetRevision,
                    CurrentRevision =
                        result_.Applied
                            ? result_.TargetRevision
                            : result_.PreviousRevision,
                    SemanticDigest =
                        result_.SemanticDigest
                };

            return CommandResult.Ok(
                SpecJsonSerializer.Serialize(
                    dto));
        }

        StringBuilder builder =
            new();

        builder.AppendLine(
            $"# Spec Plan: `{specId_.Value}`");
        builder.AppendLine();
        builder.AppendLine(
            $"- Spec ID: `{specId_.Value}`");
        builder.AppendLine(
            $"- Artifact: `{result_.ArtifactKind}`");
        builder.AppendLine(
            $"- Mode: `{result_.Mode}`");
        builder.AppendLine(
            $"- Changed: `{result_.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine(
            $"- Applied: `{result_.Applied.ToString().ToLowerInvariant()}`");

        if (result_.PreviousRevision is not null)
        {
            builder.AppendLine(
                $"- Previous Revision: `{result_.PreviousRevision.Value.Value.ToString(CultureInfo.InvariantCulture)}`");
        }

        builder.AppendLine(
            $"- Target Revision: `{result_.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine(
            $"- Semantic Digest: `{result_.SemanticDigest}`");

        return CommandResult.Ok(
            builder
                .ToString()
                .TrimEnd());
    }
    public static CommandResult RenderApproveResult(
        SpecId specId_,
        SpecApprovalLedgerStoreResult result_,
        SpecApprovalStatus currentApprovalStatus_,
        SpecApprovalStatus proposedApprovalStatus_,
        bool isJson_)
    {
        if (isJson_)
        {
            SpecApprovalResultDto dto = new()
            {
                SpecId = specId_.Value,
                ArtifactKind = result_.Approval.ArtifactKind,
                Mode = result_.Mode,
                Changed = result_.Changed,
                Applied = result_.Applied,
                TargetRevision = result_.Approval.ArtifactRevision,
                SemanticDigest = result_.Approval.SemanticDigest,
                ApprovalId = result_.Approval.Id.Value,
                CurrentApprovalStatus = currentApprovalStatus_,
                ProposedApprovalStatus = proposedApprovalStatus_,
                LedgerPreviousRevision = result_.PreviousRevision,
                LedgerTargetRevision = result_.TargetRevision
            };
            return CommandResult.Ok(SpecJsonSerializer.Serialize(dto));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Approve: `{specId_.Value}`");
        builder.AppendLine();
        builder.AppendLine($"- Spec ID: `{specId_.Value}`");
        builder.AppendLine($"- Artifact: `{result_.Approval.ArtifactKind}`");
        builder.AppendLine($"- Mode: `{result_.Mode}`");
        builder.AppendLine($"- Changed: `{result_.Changed.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Applied: `{result_.Applied.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Target Revision: `{result_.Approval.ArtifactRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Approval ID: `{result_.Approval.Id.Value}`");
        builder.AppendLine($"- Current Approval Status: `{currentApprovalStatus_}`");
        builder.AppendLine($"- Proposed Approval Status: `{proposedApprovalStatus_}`");
        builder.AppendLine($"- Ledger Previous Revision: `{(result_.PreviousRevision is not null ? result_.PreviousRevision.Value.Value.ToString(CultureInfo.InvariantCulture) : "none")}`");
        builder.AppendLine($"- Ledger Target Revision: `{result_.TargetRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Digest: `{result_.Approval.SemanticDigest}`");
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderShowResult(
        SpecId specId_,
        string artifactSelector_,
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_,
        IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses_,
        bool isJson_)
    {
        SpecArtifactApprovalStatus? reqStatus = approvalStatuses_.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.RequirementSet);
        SpecArtifactApprovalStatus? wsStatus = approvalStatuses_.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.WorkSpec);
        SpecArtifactApprovalStatus? planStatus = approvalStatuses_.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan);

        if (isJson_)
        {
            return artifactSelector_ switch
            {
                "requirements" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowRequirementsDto
                {
                    SpecId = specId_.Value,
                    Present = snapshot_.RequirementSet is not null,
                    Revision = snapshot_.RequirementSet?.Revision,
                    SemanticDigest = snapshot_.RequirementSet is null ? null : SpecSemanticDigest.Compute(snapshot_.RequirementSet),
                    ApprovalStatus = reqStatus?.Status,
                    Content = snapshot_.RequirementSet
                })),
                "work-spec" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowWorkSpecDto
                {
                    SpecId = specId_.Value,
                    Present = snapshot_.WorkSpec is not null,
                    Stale = snapshot_.WorkSpec is null ? null : snapshot_.IsWorkSpecStale,
                    Revision = snapshot_.WorkSpec?.Revision,
                    SemanticDigest = snapshot_.WorkSpec is null ? null : SpecSemanticDigest.Compute(snapshot_.WorkSpec),
                    ApprovalStatus = wsStatus?.Status,
                    Content = snapshot_.WorkSpec
                })),
                "implementation-plan" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowImplementationPlanDto
                {
                    SpecId = specId_.Value,
                    Present = snapshot_.ImplementationPlan is not null,
                    Stale = snapshot_.ImplementationPlan is null ? null : snapshot_.IsImplementationPlanStale,
                    Revision = snapshot_.ImplementationPlan?.Revision,
                    SemanticDigest = snapshot_.ImplementationPlan is null ? null : SpecSemanticDigest.Compute(snapshot_.ImplementationPlan),
                    ApprovalStatus = planStatus?.Status,
                    Content = snapshot_.ImplementationPlan
                })),                "approvals" => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowApprovalsDto
                {
                    SpecId = specId_.Value,
                    Present = ledger_ is not null,
                    Revision = ledger_?.Revision,
                    ApprovalCount = ledger_?.Approvals.Count,
                    Content = ledger_
                })),
                _ => CommandResult.Ok(SpecJsonSerializer.Serialize(new SpecShowAllDto
                {
                    SpecId = specId_.Value,
                    ApprovalStatuses = approvalStatuses_,
                    Requirements = new SpecShowRequirementsDto
                    {
                        SpecId = specId_.Value,
                        Present = snapshot_.RequirementSet is not null,
                        Revision = snapshot_.RequirementSet?.Revision,
                        SemanticDigest = snapshot_.RequirementSet is null ? null : SpecSemanticDigest.Compute(snapshot_.RequirementSet),
                        ApprovalStatus = reqStatus?.Status,
                        Content = snapshot_.RequirementSet
                    },
                    WorkSpec = new SpecShowWorkSpecDto
                    {
                        SpecId = specId_.Value,
                        Present = snapshot_.WorkSpec is not null,
                        Stale = snapshot_.WorkSpec is null ? null : snapshot_.IsWorkSpecStale,
                        Revision = snapshot_.WorkSpec?.Revision,
                        SemanticDigest = snapshot_.WorkSpec is null ? null : SpecSemanticDigest.Compute(snapshot_.WorkSpec),
                        ApprovalStatus = wsStatus?.Status,
                        Content = snapshot_.WorkSpec
                    },
                    ImplementationPlan = snapshot_.ImplementationPlan is null ? null : new SpecShowImplementationPlanDto
                    {
                        SpecId = specId_.Value,
                        Present = true,
                        Stale = snapshot_.IsImplementationPlanStale,
                        Revision = snapshot_.ImplementationPlan.Revision,
                        SemanticDigest = SpecSemanticDigest.Compute(snapshot_.ImplementationPlan),
                        ApprovalStatus = planStatus?.Status,
                        Content = snapshot_.ImplementationPlan
                    },
                    Approvals = new SpecShowApprovalsDto
                    {
                        SpecId = specId_.Value,
                        Present = ledger_ is not null,
                        Revision = ledger_?.Revision,
                        ApprovalCount = ledger_?.Approvals.Count,
                        Content = ledger_
                    }
                }))
            };
        }

        StringBuilder builder = new();
        switch (artifactSelector_)
        {
            case "requirements":
                builder.AppendLine($"# Spec Requirements: `{specId_.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(snapshot_.RequirementSet is not null).ToString().ToLowerInvariant()}`");
                if (snapshot_.RequirementSet is not null)
                {
                    builder.AppendLine($"- Revision: `{snapshot_.RequirementSet.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Approval Status: `{reqStatus?.Status.ToString() ?? "NotApproved"}`");
                    builder.AppendLine($"- Semantic Digest: `{SpecSemanticDigest.Compute(snapshot_.RequirementSet)}`");
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot_.RequirementSet));
                }
                break;

            case "work-spec":
                builder.AppendLine($"# Spec Work Spec: `{specId_.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(snapshot_.WorkSpec is not null).ToString().ToLowerInvariant()}`");
                if (snapshot_.WorkSpec is not null)
                {
                    builder.AppendLine($"- Revision: `{snapshot_.WorkSpec.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Stale: `{snapshot_.IsWorkSpecStale.ToString().ToLowerInvariant()}`");
                    builder.AppendLine($"- Approval Status: `{wsStatus?.Status.ToString() ?? "NotApproved"}`");
                    builder.AppendLine($"- Semantic Digest: `{SpecSemanticDigest.Compute(snapshot_.WorkSpec)}`");
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot_.WorkSpec));
                }
                break;

            case "implementation-plan":
                builder.AppendLine($"# Spec Implementation Plan: `{specId_.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(snapshot_.ImplementationPlan is not null).ToString().ToLowerInvariant()}`");

                if (snapshot_.ImplementationPlan is not null)
                {
                    builder.AppendLine($"- Revision: `{snapshot_.ImplementationPlan.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Stale: `{snapshot_.IsImplementationPlanStale.ToString().ToLowerInvariant()}`");
                    builder.AppendLine($"- Approval Status: `{planStatus?.Status.ToString() ?? "NotApproved"}`");
                    builder.AppendLine($"- Semantic Digest: `{SpecSemanticDigest.Compute(snapshot_.ImplementationPlan)}`");
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot_.ImplementationPlan));
                }

                break;
            case "approvals":
                builder.AppendLine($"# Spec Approvals: `{specId_.Value}`");
                builder.AppendLine();
                builder.AppendLine($"- Present: `{(ledger_ is not null).ToString().ToLowerInvariant()}`");
                if (ledger_ is not null)
                {
                    builder.AppendLine($"- Ledger Revision: `{ledger_.Revision.Value.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine($"- Approvals Count: `{ledger_.Approvals.Count.ToString(CultureInfo.InvariantCulture)}`");
                    builder.AppendLine();
                    builder.AppendLine("## Ledger Approvals");
                    builder.AppendLine();
                    if (ledger_.Approvals.Count == 0)
                    {
                        builder.AppendLine("_None._");
                    }
                    else
                    {
                        foreach (Approval approval in ledger_.Approvals)
                        {
                            builder.AppendLine($"- `{approval.Id.Value}`: `{approval.ArtifactKind}` rev `{approval.ArtifactRevision.Value.ToString(CultureInfo.InvariantCulture)}` (Digest: `{approval.SemanticDigest}`)");
                        }
                    }
                }
                break;

            default:
                builder.AppendLine($"# Spec: `{specId_.Value}`");
                builder.AppendLine();
                builder.AppendLine("## Lifecycle State");
                builder.AppendLine();
                builder.AppendLine($"- Requirements: {(snapshot_.RequirementSet is not null ? $"Present (rev {snapshot_.RequirementSet.Revision.Value.ToString(CultureInfo.InvariantCulture)}, status: {reqStatus?.Status.ToString() ?? "NotApproved"})" : "Not present")}");
                builder.AppendLine($"- Work Spec: {(snapshot_.WorkSpec is not null ? $"Present (rev {snapshot_.WorkSpec.Revision.Value.ToString(CultureInfo.InvariantCulture)}, status: {wsStatus?.Status.ToString() ?? "NotApproved"}, stale: {snapshot_.IsWorkSpecStale.ToString().ToLowerInvariant()})" : "Not present")}");
                builder.AppendLine($"- Implementation Plan: {(snapshot_.ImplementationPlan is not null ? $"Present (rev {snapshot_.ImplementationPlan.Revision.Value.ToString(CultureInfo.InvariantCulture)}, status: {planStatus?.Status.ToString() ?? "NotApproved"}, stale: {snapshot_.IsImplementationPlanStale.ToString().ToLowerInvariant()})" : "Not present")}");
                builder.AppendLine($"- Approval Ledger: {(ledger_ is not null ? $"Present (rev {ledger_.Revision.Value.ToString(CultureInfo.InvariantCulture)}, {ledger_.Approvals.Count.ToString(CultureInfo.InvariantCulture)} approvals)" : "Not present")}");

                if (snapshot_.RequirementSet is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot_.RequirementSet));
                }

                if (snapshot_.WorkSpec is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot_.WorkSpec));
                }

                if (snapshot_.ImplementationPlan is not null)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.Append(SpecMarkdownProjector.Project(snapshot_.ImplementationPlan));
                }

                if (ledger_ is not null && ledger_.Approvals.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("---");
                    builder.AppendLine();
                    builder.AppendLine("## Ledger Approvals");
                    builder.AppendLine();
                    foreach (Approval approval in ledger_.Approvals)
                    {
                        builder.AppendLine($"- `{approval.Id.Value}`: `{approval.ArtifactKind}` rev `{approval.ArtifactRevision.Value.ToString(CultureInfo.InvariantCulture)}` (Digest: `{approval.SemanticDigest}`)");
                    }
                }
                break;
        }

        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    public static CommandResult RenderDiffResult(SpecDiffResult result_, bool isJson_)
    {
        if (isJson_)
        {
            return CommandResult.Ok(SpecJsonSerializer.Serialize(result_));
        }

        StringBuilder builder = new();
        builder.AppendLine($"# Spec Diff: `{result_.SpecId}`");
        builder.AppendLine();
        builder.AppendLine($"- Artifact: `{result_.ArtifactKind}`");
        builder.AppendLine($"- Current Revision: `{(result_.CurrentRevision is not null ? result_.CurrentRevision.Value.Value.ToString(CultureInfo.InvariantCulture) : "none")}`");
        builder.AppendLine($"- Proposed Revision: `{result_.ProposedRevision.Value.ToString(CultureInfo.InvariantCulture)}`");
        builder.AppendLine($"- Semantic Changed: `{result_.SemanticChanged.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Ordering Changed: `{result_.OrderingChanged.ToString().ToLowerInvariant()}`");
        builder.AppendLine($"- Current Semantic Digest: `{(result_.CurrentSemanticDigest is not null ? result_.CurrentSemanticDigest : "none")}`");
        builder.AppendLine($"- Candidate Semantic Digest: `{result_.CandidateSemanticDigest}`");

        builder.AppendLine();
        builder.AppendLine("## Entity Changes");
        builder.AppendLine();
        if (result_.EntityChanges.Count == 0)
        {
            builder.AppendLine("_None._");
        }
        else
        {
            foreach (SpecEntityChange change in result_.EntityChanges)
            {
                string label = change.ChangeKind switch
                {
                    SpecChangeKind.Added => "ADDED",
                    SpecChangeKind.Modified => "MODIFIED",
                    SpecChangeKind.Removed => "REMOVED",
                    SpecChangeKind.Unchanged => "UNCHANGED",
                    _ => change.ChangeKind.ToString().ToUpperInvariant()
                };
                builder.AppendLine($"- [{label}] `{change.EntityId}` ({change.EntityKind})");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Reference Impacts");
        builder.AppendLine();
        if (result_.ReferenceImpacts.Count == 0)
        {
            builder.AppendLine("_None._");
        }
        else
        {
            foreach (SpecReferenceImpact impact in result_.ReferenceImpacts)
            {
                string refs = string.Join(", ", impact.ReferencedChangedIds.Select(id_ => $"`{id_}`"));
                builder.AppendLine($"- {impact.ArtifactKind} {impact.EntityKind} `{impact.EntityId}` references {refs}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Approval Impacts");
        builder.AppendLine();
        if (result_.ApprovalImpacts.Count == 0)
        {
            builder.AppendLine("_None._");
        }
        else
        {
            foreach (SpecApprovalImpact impact in result_.ApprovalImpacts)
            {
                builder.AppendLine($"- {impact.ArtifactKind}: `{impact.CurrentStatus}` -> `{impact.ProposedStatus}` (affected: {impact.Affected.ToString().ToLowerInvariant()})");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Derived Artifact Impacts");
        builder.AppendLine();
        if (result_.DerivedArtifactImpacts.Count == 0)
        {
            builder.AppendLine("_None._");
        }
        else
        {
            foreach (SpecDerivedArtifactImpact impact in result_.DerivedArtifactImpacts)
            {
                builder.AppendLine($"- {impact.ArtifactKind}: affected: {impact.Affected.ToString().ToLowerInvariant()}");
            }
        }

        return CommandResult.Ok(builder.ToString().TrimEnd());
    }
}
