namespace AiRepoKit.Spec;

public static class WorkSpecValidator
{
    private const string _constraintPrefix =
        "CON-";

    private const string _acceptanceCriterionPrefix =
        "AC-";

    private const string _requirementPrefix =
        "REQ-";

    public static IReadOnlyList<SpecValidationError> Validate(
        WorkSpec workSpec_,
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            workSpec_);

        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        List<SpecValidationError> errors =
            [];

        ValidateSchema(
            workSpec_,
            errors);

        SpecArtifactValidator.ValidateIdentity(
            workSpec_.ArtifactIdentity,
            SpecArtifactIdentity.WorkSpec,
            "WorkSpec",
            errors);

        ValidateRevisionBinding(
            workSpec_,
            requirementSet_,
            errors);

        HashSet<string> requirementIds =
            requirementSet_
                .Requirements
                .Where(
                    requirement_ =>
                        IsKind(
                            requirement_.Id,
                            _requirementPrefix))
                .Select(
                    requirement_ =>
                        requirement_.Id.Value)
                .ToHashSet(
                    StringComparer.Ordinal);

        ValidateConstraints(
            workSpec_,
            requirementIds,
            errors);

        ValidateAcceptanceCriteria(
            workSpec_,
            requirementIds,
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
        WorkSpec workSpec_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                workSpec_.SchemaId,
                SpecSchema.SchemaId,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaId,
                    Message =
                        $"Unsupported schema ID '{workSpec_.SchemaId}'."
                });
        }

        if (workSpec_.SchemaVersion !=
            SpecSchema.SchemaVersion)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaVersion,
                    Message =
                        $"Unsupported schema version '{workSpec_.SchemaVersion}'."
                });
        }
    }

    private static void ValidateRevisionBinding(
        WorkSpec workSpec_,
        RequirementSet requirementSet_,
        List<SpecValidationError> errors_)
    {
        if (workSpec_.RequirementSetRevision !=
            requirementSet_.Revision)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.RevisionMismatch,
                    Message =
                        $"WorkSpec binds RequirementSet revision '{workSpec_.RequirementSetRevision}' but supplied RequirementSet revision is '{requirementSet_.Revision}'."
                });
        }
    }

    private static void ValidateConstraints(
        WorkSpec workSpec_,
        HashSet<string> requirementIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> constraintIds =
            new(
                StringComparer.Ordinal);

        foreach (Constraint constraint in
                 workSpec_.Constraints)
        {
            ValidateEntityId(
                constraint.Id,
                _constraintPrefix,
                "Constraint",
                constraintIds,
                errors_);

            ValidateRequirementReferences(
                constraint.Id,
                constraint.RequirementIds,
                requirementIds_,
                errors_);
        }
    }

    private static void ValidateAcceptanceCriteria(
        WorkSpec workSpec_,
        HashSet<string> requirementIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> acceptanceCriterionIds =
            new(
                StringComparer.Ordinal);

        foreach (AcceptanceCriterion acceptanceCriterion in
                 workSpec_.AcceptanceCriteria)
        {
            ValidateEntityId(
                acceptanceCriterion.Id,
                _acceptanceCriterionPrefix,
                "Acceptance criterion",
                acceptanceCriterionIds,
                errors_);

            ValidateRequirementReferences(
                acceptanceCriterion.Id,
                acceptanceCriterion.RequirementIds,
                requirementIds_,
                errors_);
        }
    }

    private static void ValidateEntityId(
        StableEntityId entityId_,
        string expectedPrefix_,
        string entityLabel_,
        HashSet<string> seenIds_,
        List<SpecValidationError> errors_)
    {
        string entityId =
            entityId_.Value;

        if (!IsKind(
                entityId_,
                expectedPrefix_))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.InvalidEntityKind,
                    SourceEntityId =
                        entityId,
                    Message =
                        $"{entityLabel_} ID '{entityId}' must use the {expectedPrefix_.TrimEnd('-')} prefix."
                });

            return;
        }

        if (!seenIds_.Add(
                entityId))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.DuplicateEntityId,
                    SourceEntityId =
                        entityId,
                    Message =
                        $"Duplicate {entityLabel_.ToLowerInvariant()} ID '{entityId}'."
                });
        }
    }

    private static void ValidateRequirementReferences(
        StableEntityId sourceEntityId_,
        IReadOnlyList<StableEntityId> requirementIds_,
        HashSet<string> knownRequirementIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> seenReferences =
            new(
                StringComparer.Ordinal);

        foreach (StableEntityId requirementId in
                 requirementIds_)
        {
            string targetId =
                requirementId.Value;

            if (!IsKind(
                    requirementId,
                    _requirementPrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidReferenceTargetKind,
                        SourceEntityId =
                            sourceEntityId_.Value,
                        TargetEntityId =
                            targetId,
                        Message =
                            $"Entity '{sourceEntityId_.Value}' reference '{targetId}' must target a requirement."
                    });

                continue;
            }

            if (!seenReferences.Add(
                    targetId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateReference,
                        SourceEntityId =
                            sourceEntityId_.Value,
                        TargetEntityId =
                            targetId,
                        Message =
                            $"Entity '{sourceEntityId_.Value}' contains duplicate requirement reference '{targetId}'."
                    });

                continue;
            }

            if (!knownRequirementIds_.Contains(
                    targetId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DanglingReference,
                        SourceEntityId =
                            sourceEntityId_.Value,
                        TargetEntityId =
                            targetId,
                        Message =
                            $"Entity '{sourceEntityId_.Value}' references missing requirement '{targetId}'."
                    });
            }
        }
    }

    private static bool IsKind(
        StableEntityId entityId_,
        string prefix_)
    {
        return
            StableEntityId.IsValid(
                entityId_.Value) &&
            entityId_.Value.StartsWith(
                prefix_,
                StringComparison.Ordinal);
    }
}
