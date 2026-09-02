namespace AiRepoKit.Spec;

public static class RequirementSetValidator
{
    private const string _requirementInputPrefix =
        "INPUT-";

    private const string _requirementPrefix =
        "REQ-";

    public static IReadOnlyList<SpecValidationError> Validate(
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        List<SpecValidationError> errors =
            [];

        ValidateSchema(
            requirementSet_,
            errors);

        SpecArtifactValidator.ValidateIdentity(
            requirementSet_.ArtifactIdentity,
            SpecArtifactIdentity.RequirementSet,
            "RequirementSet",
            errors);

        HashSet<string> inputIds =
            ValidateInputs(
                requirementSet_,
                errors);

        ValidateRequirements(
            requirementSet_,
            inputIds,
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
        RequirementSet requirementSet_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                requirementSet_.SchemaId,
                SpecSchema.SchemaId,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaId,
                    Message =
                        $"Unsupported schema ID '{requirementSet_.SchemaId}'."
                });
        }

        if (requirementSet_.SchemaVersion !=
            SpecSchema.SchemaVersion)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaVersion,
                    Message =
                        $"Unsupported schema version '{requirementSet_.SchemaVersion}'."
                });
        }
    }

    private static HashSet<string> ValidateInputs(
        RequirementSet requirementSet_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> inputIds =
            new(
                StringComparer.Ordinal);

        foreach (RequirementInput input in
                 requirementSet_.Inputs)
        {
            string inputId =
                input.Id.Value;

            if (!IsKind(
                    input.Id,
                    _requirementInputPrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidEntityKind,
                        SourceEntityId =
                            inputId,
                        Message =
                            $"Requirement input ID '{inputId}' must use the INPUT prefix."
                    });

                continue;
            }

            if (!inputIds.Add(
                    inputId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateEntityId,
                        SourceEntityId =
                            inputId,
                        Message =
                            $"Duplicate requirement input ID '{inputId}'."
                    });
            }
        }

        return inputIds;
    }

    private static void ValidateRequirements(
        RequirementSet requirementSet_,
        HashSet<string> inputIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> requirementIds =
            new(
                StringComparer.Ordinal);

        foreach (Requirement requirement in
                 requirementSet_.Requirements)
        {
            string requirementId =
                requirement.Id.Value;

            if (!IsKind(
                    requirement.Id,
                    _requirementPrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidEntityKind,
                        SourceEntityId =
                            requirementId,
                        Message =
                            $"Requirement ID '{requirementId}' must use the REQ prefix."
                    });
            }
            else if (!requirementIds.Add(
                         requirementId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateEntityId,
                        SourceEntityId =
                            requirementId,
                        Message =
                            $"Duplicate requirement ID '{requirementId}'."
                    });
            }

            ValidateSourceReferences(
                requirement,
                inputIds_,
                errors_);
        }
    }

    private static void ValidateSourceReferences(
        Requirement requirement_,
        HashSet<string> inputIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> seenReferences =
            new(
                StringComparer.Ordinal);

        foreach (StableEntityId sourceInputId in
                 requirement_.SourceInputIds)
        {
            string targetId =
                sourceInputId.Value;

            if (!IsKind(
                    sourceInputId,
                    _requirementInputPrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidReferenceTargetKind,
                        SourceEntityId =
                            requirement_.Id.Value,
                        TargetEntityId =
                            targetId,
                        Message =
                            $"Requirement '{requirement_.Id.Value}' source reference '{targetId}' must target a requirement input."
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
                            requirement_.Id.Value,
                        TargetEntityId =
                            targetId,
                        Message =
                            $"Requirement '{requirement_.Id.Value}' contains duplicate source reference '{targetId}'."
                    });

                continue;
            }

            if (!inputIds_.Contains(
                    targetId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DanglingReference,
                        SourceEntityId =
                            requirement_.Id.Value,
                        TargetEntityId =
                            targetId,
                        Message =
                            $"Requirement '{requirement_.Id.Value}' references missing requirement input '{targetId}'."
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
