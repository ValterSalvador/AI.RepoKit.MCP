namespace AiRepoKit.Spec;

public static class ApprovalValidator
{
    private const string _approvalPrefix =
        "APR-";

    public static IReadOnlyList<SpecValidationError> Validate(
        Approval approval_)
    {
        ArgumentNullException.ThrowIfNull(
            approval_);

        List<SpecValidationError> errors =
            [];

        ValidateSchema(
            approval_,
            errors);

        ValidateIdentity(
            approval_,
            errors);

        ValidateArtifactBinding(
            approval_,
            errors);

        ValidateCanonicalization(
            approval_,
            errors);

        ValidateDigest(
            approval_,
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
        Approval approval_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                approval_.SchemaId,
                SpecSchema.SchemaId,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaId,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Unsupported schema ID '{approval_.SchemaId}'."
                });
        }

        if (approval_.SchemaVersion !=
            SpecSchema.SchemaVersion)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaVersion,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Unsupported schema version '{approval_.SchemaVersion}'."
                });
        }
    }

    private static void ValidateIdentity(
        Approval approval_,
        List<SpecValidationError> errors_)
    {
        if (!approval_.Id.Value.StartsWith(
                _approvalPrefix,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.InvalidEntityKind,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval ID '{approval_.Id.Value}' must use the APR prefix."
                });
        }
    }

    private static void ValidateArtifactBinding(
        Approval approval_,
        List<SpecValidationError> errors_)
    {
        if (!Enum.IsDefined(
                approval_.ArtifactKind))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.InvalidArtifactKind,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' contains invalid artifact kind '{approval_.ArtifactKind}'."
                });
        }

        string? expectedArtifactIdentity =
            approval_.ArtifactKind switch
            {
                SpecArtifactKind.RequirementSet =>
                    SpecArtifactIdentity.RequirementSet,
                SpecArtifactKind.WorkSpec =>
                    SpecArtifactIdentity.WorkSpec,
                SpecArtifactKind.ImplementationPlan =>
                    SpecArtifactIdentity.ImplementationPlan,
                _ =>
                    null
            };

        if (expectedArtifactIdentity is not null &&
            !string.Equals(
                approval_.ArtifactIdentity,
                expectedArtifactIdentity,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.ArtifactIdentityMismatch,
                    SourceEntityId =
                        approval_.Id.Value,
                    TargetEntityId =
                        approval_.ArtifactIdentity,
                    Message =
                        $"Approval '{approval_.Id.Value}' artifact identity '{approval_.ArtifactIdentity}' does not match artifact kind '{approval_.ArtifactKind}'."
                });
        }

        if (!approval_.ArtifactRevision.IsValid)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.InvalidRevision,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' must bind a positive artifact revision."
                });
        }
    }

    private static void ValidateCanonicalization(
        Approval approval_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                approval_.CanonicalizationId,
                SpecSchema.CanonicalizationId,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedCanonicalizationId,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Unsupported canonicalization ID '{approval_.CanonicalizationId}'."
                });
        }

        if (approval_.CanonicalizationVersion !=
            SpecSchema.CanonicalizationVersion)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedCanonicalizationVersion,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Unsupported canonicalization version '{approval_.CanonicalizationVersion}'."
                });
        }

        if (string.IsNullOrEmpty(
                approval_.CanonicalSemanticRepresentation))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.MissingCanonicalRepresentation,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' must bind a canonical semantic representation."
                });
        }
    }

    private static void ValidateDigest(
        Approval approval_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                approval_.DigestAlgorithm,
                SpecSchema.DigestAlgorithm,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedDigestAlgorithm,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Unsupported digest algorithm '{approval_.DigestAlgorithm}'."
                });
        }

        if (!IsLowercaseSha256(
                approval_.SemanticDigest))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.InvalidSemanticDigest,
                    SourceEntityId =
                        approval_.Id.Value,
                    Message =
                        $"Approval '{approval_.Id.Value}' semantic digest must be 64 lowercase hexadecimal characters."
                });
        }
    }

    private static bool IsLowercaseSha256(
        string digest_)
    {
        if (digest_.Length != 64)
        {
            return false;
        }

        foreach (char character in digest_)
        {
            bool isDigit =
                character >= '0' &&
                character <= '9';

            bool isLowerHex =
                character >= 'a' &&
                character <= 'f';

            if (!isDigit &&
                !isLowerHex)
            {
                return false;
            }
        }

        return true;
    }
}
