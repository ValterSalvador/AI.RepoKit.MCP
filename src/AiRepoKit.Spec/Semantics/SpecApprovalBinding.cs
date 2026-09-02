namespace AiRepoKit.Spec;

public static class SpecApprovalBinding
{
    public static Approval Create(
        StableEntityId approvalId_,
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        string canonicalRepresentation =
            SpecSemanticCanonicalizer.Canonicalize(
                requirementSet_);

        return CreateCore(
            approvalId_,
            SpecArtifactKind.RequirementSet,
            requirementSet_.ArtifactIdentity,
            requirementSet_.Revision,
            canonicalRepresentation);
    }

    public static Approval Create(
        StableEntityId approvalId_,
        WorkSpec workSpec_)
    {
        ArgumentNullException.ThrowIfNull(
            workSpec_);

        string canonicalRepresentation =
            SpecSemanticCanonicalizer.Canonicalize(
                workSpec_);

        return CreateCore(
            approvalId_,
            SpecArtifactKind.WorkSpec,
            workSpec_.ArtifactIdentity,
            workSpec_.Revision,
            canonicalRepresentation);
    }

    public static Approval Create(
        StableEntityId approvalId_,
        ImplementationPlan implementationPlan_)
    {
        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        string canonicalRepresentation =
            SpecSemanticCanonicalizer.Canonicalize(
                implementationPlan_);

        return CreateCore(
            approvalId_,
            SpecArtifactKind.ImplementationPlan,
            implementationPlan_.ArtifactIdentity,
            implementationPlan_.Revision,
            canonicalRepresentation);
    }

    private static Approval CreateCore(
        StableEntityId approvalId_,
        SpecArtifactKind artifactKind_,
        string artifactIdentity_,
        ArtifactRevision artifactRevision_,
        string canonicalRepresentation_)
    {
        return new Approval
        {
            Id =
                approvalId_,
            ArtifactKind =
                artifactKind_,
            ArtifactIdentity =
                artifactIdentity_,
            ArtifactRevision =
                artifactRevision_,
            CanonicalSemanticRepresentation =
                canonicalRepresentation_,
            SemanticDigest =
                SpecSemanticDigest.ComputeFromCanonicalRepresentation(
                    canonicalRepresentation_)
        };
    }
}
