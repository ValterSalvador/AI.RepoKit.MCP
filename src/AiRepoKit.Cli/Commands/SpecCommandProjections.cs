using System.Text.Json.Serialization;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Commands;

public sealed record SpecMutationResultDto
{
    public required string SpecId { get; init; }

    public required SpecArtifactKind ArtifactKind { get; init; }

    public required SpecWriteMode Mode { get; init; }

    public required bool Changed { get; init; }

    public required bool Applied { get; init; }

    public ArtifactRevision? PreviousRevision { get; init; }

    public required ArtifactRevision TargetRevision { get; init; }

    public ArtifactRevision? CurrentRevision { get; init; }

    public required string SemanticDigest { get; init; }
}

public sealed record SpecApprovalResultDto
{
    public required string SpecId { get; init; }

    public required SpecArtifactKind ArtifactKind { get; init; }

    public required SpecWriteMode Mode { get; init; }

    public required bool Changed { get; init; }

    public required bool Applied { get; init; }

    public required ArtifactRevision TargetRevision { get; init; }

    public ArtifactRevision CurrentRevision => this.TargetRevision;

    public required string SemanticDigest { get; init; }

    public required string ApprovalId { get; init; }

    public required SpecApprovalStatus CurrentApprovalStatus { get; init; }

    public required SpecApprovalStatus ProposedApprovalStatus { get; init; }

    public ArtifactRevision? LedgerPreviousRevision { get; init; }

    public required ArtifactRevision LedgerTargetRevision { get; init; }
}

public sealed record SpecShowRequirementsDto
{
    public required string SpecId { get; init; }

    public required bool Present { get; init; }

    public ArtifactRevision? Revision { get; init; }

    public string? SemanticDigest { get; init; }

    public SpecApprovalStatus? ApprovalStatus { get; init; }

    public RequirementSet? Content { get; init; }
}

public sealed record SpecShowWorkSpecDto
{
    public required string SpecId { get; init; }

    public required bool Present { get; init; }

    public bool? Stale { get; init; }

    public ArtifactRevision? Revision { get; init; }

    public string? SemanticDigest { get; init; }

    public SpecApprovalStatus? ApprovalStatus { get; init; }

    public WorkSpec? Content { get; init; }
}

public sealed record SpecShowApprovalsDto
{
    public required string SpecId { get; init; }

    public required bool Present { get; init; }

    public ArtifactRevision? Revision { get; init; }

    public int? ApprovalCount { get; init; }

    public SpecApprovalLedger? Content { get; init; }
}

public sealed record SpecShowImplementationPlanDto
{
    public required bool Present { get; init; }

    public bool? Stale { get; init; }

    public ArtifactRevision? Revision { get; init; }

    public string? SemanticDigest { get; init; }

    public SpecApprovalStatus? ApprovalStatus { get; init; }

    public ImplementationPlan? Content { get; init; }
}

public sealed record SpecShowAllDto
{
    public required string SpecId { get; init; }

    public required IReadOnlyList<SpecArtifactApprovalStatus> ApprovalStatuses { get; init; }

    public SpecShowRequirementsDto? Requirements { get; init; }

    public SpecShowWorkSpecDto? WorkSpec { get; init; }

    public SpecShowImplementationPlanDto? ImplementationPlan { get; init; }

    public SpecShowApprovalsDto? Approvals { get; init; }
}

public sealed record SpecCliErrorDto
{
    public required string Error { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ValidationErrors { get; init; }
}
