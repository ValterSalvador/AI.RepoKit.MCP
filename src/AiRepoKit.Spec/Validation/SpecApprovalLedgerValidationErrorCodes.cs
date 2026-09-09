namespace AiRepoKit.Spec;

public static class SpecApprovalLedgerValidationErrorCodes
{
    public const string InvalidSpecId =
        nameof(InvalidSpecId);

    public const string SpecIdMismatch =
        nameof(SpecIdMismatch);

    public const string MissingApprovals =
        nameof(MissingApprovals);

    public const string NullApprovalEntry =
        nameof(NullApprovalEntry);

    public const string InconsistentSemanticDigest =
        nameof(InconsistentSemanticDigest);

    public const string DuplicateApprovalBinding =
        nameof(DuplicateApprovalBinding);

    public const string ConflictingApprovalBinding =
        nameof(ConflictingApprovalBinding);
}
