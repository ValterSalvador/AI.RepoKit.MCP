using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Spec.Context;

public static class SpecContextValidator
{
    public static IReadOnlyList<SpecValidationError> Validate(
        SpecContext specContext_)
    {
        ArgumentNullException.ThrowIfNull(
            specContext_);

        List<SpecValidationError> errors =
            [];

        ValidateHeader(
            specContext_,
            errors);

        Dictionary<string, RepositoryEvidence> evidenceById =
            ValidateEvidence(
                specContext_.Evidence,
                errors);

        ValidateReferences(
            specContext_.References,
            evidenceById,
            errors);

        ValidateOmissions(
            specContext_.Omissions,
            specContext_.Truncated,
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

    private static void ValidateHeader(
        SpecContext specContext_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                specContext_.SchemaId,
                SpecContextSchema.SchemaId,
                StringComparison.Ordinal))
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.UnsupportedSpecContextSchema,
                $"Unsupported SpecContext schema ID '{specContext_.SchemaId}'.");
        }

        if (specContext_.SchemaVersion !=
            SpecContextSchema.SchemaVersion)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.UnsupportedSpecContextSchema,
                $"Unsupported SpecContext schema version '{specContext_.SchemaVersion}'.");
        }

        if (!SpecId.IsValid(
                specContext_.SpecId))
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecId,
                $"Invalid Spec ID '{specContext_.SpecId}'.");
        }

        if (!specContext_.RequirementSetRevision.IsValid)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextRevision,
                "RequirementSet revision must be greater than zero.");
        }

        if (!specContext_.WorkSpecRevision.IsValid)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextRevision,
                "WorkSpec revision must be greater than zero.");
        }

        if (specContext_.ReferenceLimit is < 1 or > 100)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextReferenceLimit,
                "Reference limit must be between 1 and 100 inclusive.");
        }

        if (specContext_.Budget <= 0)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextBudget,
                "Budget must be greater than zero.");
        }

        if (specContext_.EstimatedTokens < 0)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextTokenEstimate,
                "Estimated tokens must be non-negative.");
        }
    }

    private static Dictionary<string, RepositoryEvidence> ValidateEvidence(
        IReadOnlyList<RepositoryEvidence>? evidence_,
        List<SpecValidationError> errors_)
    {
        Dictionary<string, RepositoryEvidence> evidenceById =
            new(
                StringComparer.Ordinal);

        if (evidence_ is null)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidRepositoryEvidence,
                "Evidence collection must not be null.");

            return evidenceById;
        }

        foreach (RepositoryEvidence? evidence in evidence_)
        {
            if (evidence is null)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidRepositoryEvidence,
                    "Repository evidence must not be null.");

                continue;
            }

            string evidenceId =
                evidence.EvidenceId ??
                string.Empty;

            if (IsBlank(evidence.EvidenceId) ||
                IsBlank(evidence.Source) ||
                IsBlank(evidence.Kind) ||
                IsBlank(evidence.Reference))
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidRepositoryEvidence,
                    "Repository evidence requires nonblank EvidenceId, Source, Kind, and Reference.",
                    evidenceId);
            }

            if (!Enum.IsDefined(evidence.Availability) ||
                evidence.Availability == 0 ||
                !Enum.IsDefined(evidence.Freshness) ||
                evidence.Freshness == 0)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidRepositoryEvidenceState,
                    "Repository evidence availability and freshness must be defined nonzero values.",
                    evidenceId);
            }

            if (!evidenceById.TryAdd(
                    evidenceId,
                    evidence))
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.DuplicateRepositoryEvidenceId,
                    $"Duplicate repository evidence ID '{evidenceId}'.",
                    evidenceId);
            }

            if (evidence.Availability is
                    RepositoryEvidenceAvailability.Missing or
                    RepositoryEvidenceAvailability.Unavailable &&
                evidence.Freshness is
                    RepositoryEvidenceFreshness.Current or
                    RepositoryEvidenceFreshness.Stale)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidRepositoryEvidenceState,
                    $"Evidence '{evidenceId}' cannot claim current or stale freshness when it is not available.",
                    evidenceId);
            }
        }

        return evidenceById;
    }

    private static void ValidateReferences(
        IReadOnlyList<SpecContextReference>? references_,
        IReadOnlyDictionary<string, RepositoryEvidence> evidenceById_,
        List<SpecValidationError> errors_)
    {
        if (references_ is null)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextReference,
                "References collection must not be null.");

            return;
        }

        HashSet<(string Kind, string Reference)> identities =
            new(
                SpecContextReferenceIdentityComparer.Instance);

        foreach (SpecContextReference? reference in references_)
        {
            if (reference is null)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidSpecContextReference,
                    "SpecContext reference must not be null.");

                continue;
            }

            string evidenceId =
                reference.EvidenceId ??
                string.Empty;

            if (IsBlank(reference.EvidenceId) ||
                IsBlank(reference.Kind) ||
                IsBlank(reference.Reference) ||
                IsBlank(reference.Reason) ||
                reference.Priority < 0)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidSpecContextReference,
                    "SpecContext reference requires nonblank fields and a non-negative priority.",
                    evidenceId,
                    reference.Reference ?? string.Empty);
            }

            if (!evidenceById_.TryGetValue(
                    evidenceId,
                    out RepositoryEvidence? evidence))
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.MissingRepositoryEvidenceReference,
                    $"Reference targets missing evidence '{evidenceId}'.",
                    evidenceId,
                    reference.Reference ?? string.Empty);
            }
            else if (evidence.Availability !=
                     RepositoryEvidenceAvailability.Available)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.UnavailableRepositoryEvidenceReference,
                    $"Reference targets evidence '{evidenceId}' that is not available.",
                    evidenceId,
                    reference.Reference ?? string.Empty);
            }

            (string Kind, string Reference) identity =
                (reference.Kind ?? string.Empty, reference.Reference ?? string.Empty);

            if (!identities.Add(
                    identity))
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.DuplicateSpecContextReference,
                    $"Duplicate SpecContext reference '{identity.Kind}', '{identity.Reference}'.",
                    evidenceId,
                    identity.Reference);
            }
        }
    }

    private static void ValidateOmissions(
        IReadOnlyList<SpecContextOmission>? omissions_,
        bool truncated_,
        List<SpecValidationError> errors_)
    {
        if (omissions_ is null)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextOmission,
                "Omissions collection must not be null.");

            return;
        }

        foreach (SpecContextOmission? omission in omissions_)
        {
            if (omission is null ||
                IsBlank(omission.Reference) ||
                IsBlank(omission.Reason) ||
                omission.RemovedEstimatedTokens < 0)
            {
                AddError(
                    errors_,
                    SpecContextValidationErrorCodes.InvalidSpecContextOmission,
                    "SpecContext omission requires nonblank fields and a non-negative token estimate.",
                    targetEntityId_: omission?.Reference ?? string.Empty);
            }
        }

        if (omissions_.Count > 0 &&
            !truncated_)
        {
            AddError(
                errors_,
                SpecContextValidationErrorCodes.InvalidSpecContextTruncation,
                "SpecContext must be truncated when omissions are present.");
        }
    }

    private static bool IsBlank(
        string? value_)
    {
        return string.IsNullOrWhiteSpace(
            value_);
    }

    private static void AddError(
        List<SpecValidationError> errors_,
        string code_,
        string message_,
        string sourceEntityId_ = "",
        string targetEntityId_ = "")
    {
        errors_.Add(
            new SpecValidationError
            {
                Code =
                    code_,
                SourceEntityId =
                    sourceEntityId_,
                TargetEntityId =
                    targetEntityId_,
                Message =
                    message_
            });
    }

    private sealed class SpecContextReferenceIdentityComparer :
        IEqualityComparer<(string Kind, string Reference)>
    {
        public static SpecContextReferenceIdentityComparer Instance
        {
            get;
        } = new();

        public bool Equals(
            (string Kind, string Reference) x_,
            (string Kind, string Reference) y_)
        {
            return
                StringComparer.Ordinal.Equals(
                    x_.Kind,
                    y_.Kind) &&
                StringComparer.Ordinal.Equals(
                    x_.Reference,
                    y_.Reference);
        }

        public int GetHashCode(
            (string Kind, string Reference) obj_)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(
                    obj_.Kind),
                StringComparer.Ordinal.GetHashCode(
                    obj_.Reference));
        }
    }
}
