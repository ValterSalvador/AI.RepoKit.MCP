namespace AiRepoKit.Spec;

public static class ImplementationPlanValidator
{
    private const string _planStepPrefix =
        "PLAN-STEP-";

    private const string _requirementPrefix =
        "REQ-";

    private const string _acceptanceCriterionPrefix =
        "AC-";

    public static IReadOnlyList<SpecValidationError> Validate(
        ImplementationPlan implementationPlan_,
        WorkSpec workSpec_,
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        ArgumentNullException.ThrowIfNull(
            workSpec_);

        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        List<SpecValidationError> errors =
            [];

        ValidateSchema(
            implementationPlan_,
            errors);

        SpecArtifactValidator.ValidateIdentity(
            implementationPlan_.ArtifactIdentity,
            SpecArtifactIdentity.ImplementationPlan,
            "ImplementationPlan",
            errors);

        ValidateRevisionBinding(
            implementationPlan_,
            workSpec_,
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

        HashSet<string> acceptanceCriterionIds =
            workSpec_
                .AcceptanceCriteria
                .Where(
                    acceptanceCriterion_ =>
                        IsKind(
                            acceptanceCriterion_.Id,
                            _acceptanceCriterionPrefix))
                .Select(
                    acceptanceCriterion_ =>
                        acceptanceCriterion_.Id.Value)
                .ToHashSet(
                    StringComparer.Ordinal);

        ValidateSteps(
            implementationPlan_,
            requirementIds,
            acceptanceCriterionIds,
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
        ImplementationPlan implementationPlan_,
        List<SpecValidationError> errors_)
    {
        if (!string.Equals(
                implementationPlan_.SchemaId,
                SpecSchema.SchemaId,
                StringComparison.Ordinal))
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaId,
                    Message =
                        $"Unsupported schema ID '{implementationPlan_.SchemaId}'."
                });
        }

        if (implementationPlan_.SchemaVersion !=
            SpecSchema.SchemaVersion)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.UnsupportedSchemaVersion,
                    Message =
                        $"Unsupported schema version '{implementationPlan_.SchemaVersion}'."
                });
        }
    }

    private static void ValidateRevisionBinding(
        ImplementationPlan implementationPlan_,
        WorkSpec workSpec_,
        List<SpecValidationError> errors_)
    {
        if (implementationPlan_.WorkSpecRevision !=
            workSpec_.Revision)
        {
            errors_.Add(
                new SpecValidationError
                {
                    Code =
                        SpecValidationErrorCodes.RevisionMismatch,
                    Message =
                        $"Implementation plan binds WorkSpec revision '{implementationPlan_.WorkSpecRevision}' but supplied WorkSpec revision is '{workSpec_.Revision}'."
                });
        }
    }

    private static void ValidateSteps(
        ImplementationPlan implementationPlan_,
        HashSet<string> requirementIds_,
        HashSet<string> acceptanceCriterionIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> stepIds =
            new(
                StringComparer.Ordinal);

        foreach (PlanStep step in
                 implementationPlan_.Steps)
        {
            string stepId =
                step.Id.Value;

            if (!IsKind(
                    step.Id,
                    _planStepPrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidEntityKind,
                        SourceEntityId =
                            stepId,
                        Message =
                            $"Plan step ID '{stepId}' must use the PLAN-STEP prefix."
                    });
            }
            else if (!stepIds.Add(
                         stepId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateEntityId,
                        SourceEntityId =
                            stepId,
                        Message =
                            $"Duplicate plan step ID '{stepId}'."
                    });
            }

            ValidateReferences(
                step.Id,
                step.RequirementIds,
                _requirementPrefix,
                "requirement",
                requirementIds_,
                errors_);

            ValidateReferences(
                step.Id,
                step.AcceptanceCriterionIds,
                _acceptanceCriterionPrefix,
                "acceptance criterion",
                acceptanceCriterionIds_,
                errors_);
        }
    }

    private static void ValidateReferences(
        StableEntityId sourceEntityId_,
        IReadOnlyList<StableEntityId> references_,
        string expectedPrefix_,
        string targetLabel_,
        HashSet<string> knownTargetIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> seenReferences =
            new(
                StringComparer.Ordinal);

        foreach (StableEntityId reference in
                 references_)
        {
            string targetId =
                reference.Value;

            if (!IsKind(
                    reference,
                    expectedPrefix_))
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
                            $"Plan step '{sourceEntityId_.Value}' reference '{targetId}' must target a {targetLabel_}."
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
                            $"Plan step '{sourceEntityId_.Value}' contains duplicate {targetLabel_} reference '{targetId}'."
                    });

                continue;
            }

            if (!knownTargetIds_.Contains(
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
                            $"Plan step '{sourceEntityId_.Value}' references missing {targetLabel_} '{targetId}'."
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
