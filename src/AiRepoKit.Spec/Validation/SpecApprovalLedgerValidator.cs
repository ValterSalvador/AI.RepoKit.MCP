using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Spec;

public static class SpecApprovalLedgerValidator
{
    public static IReadOnlyList<SpecValidationError> Validate(
        SpecApprovalLedger ledger_)
    {
        return ValidateCore(
            ledger_,
            expectedSpecId_: null);
    }

    public static IReadOnlyList<SpecValidationError> Validate(
        SpecApprovalLedger ledger_,
        SpecId expectedSpecId_)
    {
        return ValidateCore(
            ledger_,
            expectedSpecId_);
    }

    private static IReadOnlyList<SpecValidationError> ValidateCore(
        SpecApprovalLedger ledger_,
        SpecId? expectedSpecId_)
    {
        ArgumentNullException.ThrowIfNull(
            ledger_);

        List<SpecValidationError> errors =
            [];

        ValidateSchema(
            ledger_,
            errors);

        ValidateIdentity(
            ledger_,
            errors);

        ValidateRevision(
            ledger_,
            errors);

        ValidateSpecId(
            ledger_,
            expectedSpecId_,
            errors);

        ValidateApprovals(
            ledger_,
            errors);

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

    private static void ValidateSchema(
        SpecApprovalLedger ledger_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                ledger_.SchemaId,
                SpecSchema.SchemaId,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaId,
                    Message =
                        $"Unsupported schema ID '{ledger_.SchemaId}'."
                });
        }

        if (ledger_.SchemaVersion !=
            SpecSchema.SchemaVersion)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaVersion,
                    Message =
                        $"Unsupported schema version '{ledger_.SchemaVersion}'."
                });
        }
    }

    private static void ValidateIdentity(
        SpecApprovalLedger ledger_,
        List<SpecValidationError> errors_)
    {
        SpecArtifactValidator.ValidateIdentity(
            ledger_.ArtifactIdentity,
            SpecApprovalLedger.DefaultArtifactIdentity,
            "SpecApprovalLedger",
            errors_);
    }

    private static void ValidateRevision(
        SpecApprovalLedger ledger_,
        List<SpecValidationError> errors_)
    {
        if (!ledger_.Revision.IsValid)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.InvalidRevision,
                    Message =
                        "Spec approval ledger must bind a positive artifact revision."
                });
        }
    }

    private static void ValidateSpecId(
        SpecApprovalLedger ledger_,
        SpecId? expectedSpecId_,
        List<SpecValidationError> errors_)
    {
        if (!SpecId.IsValid(
                ledger_.SpecId))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecApprovalLedgerValidationErrorCodes.InvalidSpecId,
                    SourceEntityId =
                        ledger_.SpecId ?? string.Empty,
                    Message =
                        $"Spec approval ledger contains invalid SpecId '{ledger_.SpecId}'."
                });
        }

        if (expectedSpecId_.HasValue &&
            !string.Equals(
                ledger_.SpecId,
                expectedSpecId_.Value.Value,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecApprovalLedgerValidationErrorCodes.SpecIdMismatch,
                    SourceEntityId =
                        ledger_.SpecId ?? string.Empty,
                    TargetEntityId =
                        expectedSpecId_.Value.Value,
                    Message =
                        $"Spec approval ledger SpecId '{ledger_.SpecId}' does not match expected workspace SpecId '{expectedSpecId_.Value.Value}'."
                });
        }
    }

    private static void ValidateApprovals(
        SpecApprovalLedger ledger_,
        List<SpecValidationError> errors_)
    {
        if (ledger_.Approvals is null)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecApprovalLedgerValidationErrorCodes.MissingApprovals,
                    Message =
                        "Spec approval ledger approvals collection is required."
                });

            return;
        }

        HashSet<string> seenApprovalIds =
            new(
                StringComparer.Ordinal);
        Dictionary<(SpecArtifactKind, string, ArtifactRevision), Approval> seenArtifactApprovals =
            [];

        foreach (Approval? approval in
                 ledger_.Approvals)
        {
            if (approval is null)
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecApprovalLedgerValidationErrorCodes.NullApprovalEntry,
                        Message =
                            "Spec approval ledger contains a null approval entry."
                    });

                continue;
            }

            IReadOnlyList<SpecValidationError> approvalErrors =
                ApprovalValidator.Validate(
                    approval);

            errors_.AddRange(
                approvalErrors);

            string approvalId =
                approval.Id.Value;

            if (!seenApprovalIds.Add(
                    approvalId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateEntityId,
                        SourceEntityId =
                            approvalId,
                        Message =
                            $"Duplicate approval ID '{approvalId}'."
                    });
            }

            if (approvalErrors.Count == 0)
            {
                string expectedDigest =
                    SpecSemanticDigest.ComputeFromCanonicalRepresentation(
                        approval.CanonicalSemanticRepresentation);

                if (!string.Equals(
                        approval.SemanticDigest,
                        expectedDigest,
                        StringComparison.Ordinal))
                {
                    errors_.Add(
                        new SpecValidationError
                        {
                            Code =
                                SpecApprovalLedgerValidationErrorCodes.InconsistentSemanticDigest,
                            SourceEntityId =
                                approvalId,
                            Message =
                                $"Approval '{approvalId}' semantic digest does not match its canonical semantic representation."
                        });
                }
            }

            if (approval.ArtifactRevision.IsValid &&
                !string.IsNullOrEmpty(
                    approval.ArtifactIdentity))
            {
                (SpecArtifactKind, string, ArtifactRevision) artifactKey =
                    (approval.ArtifactKind, approval.ArtifactIdentity, approval.ArtifactRevision);

                if (seenArtifactApprovals.TryGetValue(
                        artifactKey,
                        out Approval? existingApproval))
                {
                    bool sameSemantics =
                        string.Equals(
                            existingApproval.CanonicalizationId,
                            approval.CanonicalizationId,
                            StringComparison.Ordinal) &&
                        existingApproval.CanonicalizationVersion ==
                        approval.CanonicalizationVersion &&
                        string.Equals(
                            existingApproval.DigestAlgorithm,
                            approval.DigestAlgorithm,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            existingApproval.CanonicalSemanticRepresentation,
                            approval.CanonicalSemanticRepresentation,
                            StringComparison.Ordinal) &&
                        string.Equals(
                            existingApproval.SemanticDigest,
                            approval.SemanticDigest,
                            StringComparison.Ordinal);

                    if (sameSemantics)
                    {
                        errors_.Add(
                            new SpecValidationError
                            {
                                Code =
                                    SpecApprovalLedgerValidationErrorCodes.DuplicateApprovalBinding,
                                SourceEntityId =
                                    approvalId,
                                TargetEntityId =
                                    existingApproval.Id.Value,
                                Message =
                                    $"Spec approval ledger contains duplicate approval binding for artifact '{approval.ArtifactKind}' revision '{approval.ArtifactRevision.Value}'."
                            });
                    }
                    else
                    {
                        errors_.Add(
                            new SpecValidationError
                            {
                                Code =
                                    SpecApprovalLedgerValidationErrorCodes.ConflictingApprovalBinding,
                                SourceEntityId =
                                    approvalId,
                                TargetEntityId =
                                    existingApproval.Id.Value,
                                Message =
                                    $"Spec approval ledger contains conflicting approval binding for artifact '{approval.ArtifactKind}' revision '{approval.ArtifactRevision.Value}'."
                            });
                    }
                }
                else
                {
                    seenArtifactApprovals.Add(
                        artifactKey,
                        approval);
                }
            }
        }
    }
}
