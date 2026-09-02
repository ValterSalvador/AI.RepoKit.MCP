namespace AiRepoKit.Spec;

public static class ApprovalBindingValidator
{
    public static IReadOnlyList<SpecValidationError> Validate(
        Approval approval_,
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        return ValidateCore(
            approval_,
            SpecArtifactKind.RequirementSet,
            requirementSet_.ArtifactIdentity,
            requirementSet_.Revision,
            SpecSemanticCanonicalizer.Canonicalize(
                requirementSet_));
    }

    public static IReadOnlyList<SpecValidationError> Validate(
        Approval approval_,
        WorkSpec workSpec_)
    {
        ArgumentNullException.ThrowIfNull(
            workSpec_);

        return ValidateCore(
            approval_,
            SpecArtifactKind.WorkSpec,
            workSpec_.ArtifactIdentity,
            workSpec_.Revision,
            SpecSemanticCanonicalizer.Canonicalize(
                workSpec_));
    }

    public static IReadOnlyList<SpecValidationError> Validate(
        Approval approval_,
        ImplementationPlan implementationPlan_)
    {
        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        return ValidateCore(
            approval_,
            SpecArtifactKind.ImplementationPlan,
            implementationPlan_.ArtifactIdentity,
            implementationPlan_.Revision,
            SpecSemanticCanonicalizer.Canonicalize(
                implementationPlan_));
    }

    private static IReadOnlyList<SpecValidationError> ValidateCore(
        Approval approval_,
        SpecArtifactKind expectedKind_,
        string expectedIdentity_,
        ArtifactRevision expectedRevision_,
        string expectedCanonicalRepresentation_)
    {
        ArgumentNullException.ThrowIfNull(
            approval_);

        List<SpecValidationError> errors =
            ApprovalValidator
                .Validate(
                    approval_)
                .ToList();

        if (approval_.ArtifactKind !=
            expectedKind_)
        {
            errors.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.ArtifactKindMismatch,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' artifact kind '{approval_.ArtifactKind}' does not match '{expectedKind_}'."
                });
        }

        if (!string.Equals(
                approval_.ArtifactIdentity,
                expectedIdentity_,
                StringComparison.Ordinal))
        {
            errors.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.ArtifactIdentityMismatch,
                    SourceEntityId =
                        approval_.Id.Value,
                    TargetEntityId =
                        approval_.ArtifactIdentity,
                    Message =
                        $"Approval '{approval_.Id.Value}' artifact identity '{approval_.ArtifactIdentity}' does not match '{expectedIdentity_}'."
                });
        }

        if (approval_.ArtifactRevision !=
            expectedRevision_)
        {
            errors.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.RevisionMismatch,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' artifact revision '{approval_.ArtifactRevision}' does not match '{expectedRevision_}'."
                });
        }

        if (!string.Equals(
                approval_.CanonicalSemanticRepresentation,
                expectedCanonicalRepresentation_,
                StringComparison.Ordinal))
        {
            errors.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.CanonicalRepresentationMismatch,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' canonical semantic representation does not match the artifact."
                });
        }

        string expectedDigest =
            SpecSemanticDigest.ComputeFromCanonicalRepresentation(
                expectedCanonicalRepresentation_);

        if (!string.Equals(
                approval_.SemanticDigest,
                expectedDigest,
                StringComparison.Ordinal))
        {
            errors.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.SemanticDigestMismatch,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' semantic digest does not match the artifact."
                });
        }

        return errors
            .OrderBy(
                error_ =>
                    error_.Code,
                StringComparer.Ordinal)
            .ThenBy(
                error_ =>
                    error_.SourceEntityId,
                StringComparer.Ordinal)
            .ThenBy(
                error_ =>
                    error_.TargetEntityId,
                StringComparer.Ordinal)
            .ThenBy(
                error_ =>
                    error_.Message,
                StringComparer.Ordinal)
            .ToArray();
    }
}
