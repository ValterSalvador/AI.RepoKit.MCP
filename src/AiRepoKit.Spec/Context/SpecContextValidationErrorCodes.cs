namespace AiRepoKit.Spec.Context;

public static class SpecContextValidationErrorCodes
{
    public const string UnsupportedSpecContextSchema =
        "SPEC_CONTEXT_UNSUPPORTED_SCHEMA";

    public const string InvalidSpecId =
        "SPEC_CONTEXT_INVALID_SPEC_ID";

    public const string InvalidSpecContextRevision =
        "SPEC_CONTEXT_INVALID_REVISION";

    public const string InvalidSpecContextBudget =
        "SPEC_CONTEXT_INVALID_BUDGET";

    public const string InvalidSpecContextReferenceLimit =
        "SPEC_CONTEXT_INVALID_REFERENCE_LIMIT";

    public const string InvalidSpecContextTokenEstimate =
        "SPEC_CONTEXT_INVALID_TOKEN_ESTIMATE";

    public const string InvalidRepositoryEvidence =
        "SPEC_CONTEXT_INVALID_REPOSITORY_EVIDENCE";

    public const string DuplicateRepositoryEvidenceId =
        "SPEC_CONTEXT_DUPLICATE_REPOSITORY_EVIDENCE_ID";

    public const string InvalidRepositoryEvidenceState =
        "SPEC_CONTEXT_INVALID_REPOSITORY_EVIDENCE_STATE";

    public const string InvalidSpecContextReference =
        "SPEC_CONTEXT_INVALID_REFERENCE";

    public const string MissingRepositoryEvidenceReference =
        "SPEC_CONTEXT_MISSING_EVIDENCE_REFERENCE";

    public const string UnavailableRepositoryEvidenceReference =
        "SPEC_CONTEXT_UNAVAILABLE_EVIDENCE_REFERENCE";

    public const string DuplicateSpecContextReference =
        "SPEC_CONTEXT_DUPLICATE_REFERENCE";

    public const string InvalidSpecContextOmission =
        "SPEC_CONTEXT_INVALID_OMISSION";

    public const string InvalidSpecContextTruncation =
        "SPEC_CONTEXT_INVALID_TRUNCATION";
}
