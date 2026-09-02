namespace AiRepoKit.Spec;

public static class SpecValidationErrorCodes
{
    public const string DanglingReference =
        nameof(DanglingReference);

    public const string DuplicateEntityId =
        nameof(DuplicateEntityId);

    public const string DuplicateReference =
        nameof(DuplicateReference);

    public const string InvalidEntityKind =
        nameof(InvalidEntityKind);

    public const string InvalidReferenceTargetKind =
        nameof(InvalidReferenceTargetKind);

    public const string RevisionMismatch =
        nameof(RevisionMismatch);

    public const string UnsupportedSchemaId =
        nameof(UnsupportedSchemaId);

    public const string UnsupportedSchemaVersion =
        nameof(UnsupportedSchemaVersion);
    public const string InvalidArtifactKind =
        nameof(InvalidArtifactKind);

    public const string InvalidRevision =
        nameof(InvalidRevision);

    public const string InvalidSemanticDigest =
        nameof(InvalidSemanticDigest);

    public const string MissingCanonicalRepresentation =
        nameof(MissingCanonicalRepresentation);

    public const string UnsupportedCanonicalizationId =
        nameof(UnsupportedCanonicalizationId);

    public const string UnsupportedCanonicalizationVersion =
        nameof(UnsupportedCanonicalizationVersion);

    public const string UnsupportedDigestAlgorithm =
        nameof(UnsupportedDigestAlgorithm);

    public const string ArtifactIdentityMismatch =
        nameof(ArtifactIdentityMismatch);

    public const string InvalidVerificationStatus =
        nameof(InvalidVerificationStatus);

    public const string ArtifactKindMismatch =
        nameof(ArtifactKindMismatch);

    public const string CanonicalRepresentationMismatch =
        nameof(CanonicalRepresentationMismatch);

    public const string SemanticDigestMismatch =
        nameof(SemanticDigestMismatch);

}
