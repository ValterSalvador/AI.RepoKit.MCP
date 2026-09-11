namespace AiRepoKit.Spec.Diff;

public enum SpecChangeKind
{
    Added,
    Modified,
    Removed,
    Unchanged
}

public enum SpecDiffEntityKind
{
    RequirementInput,
    Requirement,
    Constraint,
    AcceptanceCriterion,
    PlanStep
}

public enum SpecDerivedArtifactKind
{
    SpecContext,
    ImplementationChecklist
}

public sealed record SpecEntityChange
{
    public required SpecDiffEntityKind EntityKind { get; init; }

    public required string EntityId { get; init; }

    public required SpecChangeKind ChangeKind { get; init; }
}

public sealed record SpecReferenceImpact
{
    public required SpecArtifactKind ArtifactKind { get; init; }

    public required SpecDiffEntityKind EntityKind { get; init; }

    public required string EntityId { get; init; }

    public required IReadOnlyList<string> ReferencedChangedIds { get; init; }
}

public sealed record SpecApprovalImpact
{
    public required SpecArtifactKind ArtifactKind { get; init; }

    public required SpecApprovalStatus CurrentStatus { get; init; }

    public required SpecApprovalStatus ProposedStatus { get; init; }

    public required bool Affected { get; init; }
}

public sealed record SpecDerivedArtifactImpact
{
    public required SpecDerivedArtifactKind ArtifactKind { get; init; }

    public required bool Affected { get; init; }
}

public sealed record SpecDiffResult
{
    public required string SpecId { get; init; }

    public required SpecArtifactKind ArtifactKind { get; init; }

    public required string ArtifactIdentity { get; init; }

    public ArtifactRevision? CurrentRevision { get; init; }

    public required ArtifactRevision ProposedRevision { get; init; }

    public string? CurrentSemanticDigest { get; init; }

    public required string CandidateSemanticDigest { get; init; }

    public required bool SemanticChanged { get; init; }

    public required bool OrderingChanged { get; init; }

    public required IReadOnlyList<SpecEntityChange> EntityChanges { get; init; }

    public required IReadOnlyList<SpecReferenceImpact> ReferenceImpacts { get; init; }

    public required IReadOnlyList<SpecApprovalImpact> ApprovalImpacts { get; init; }

    public required IReadOnlyList<SpecDerivedArtifactImpact> DerivedArtifactImpacts { get; init; }
}
