namespace AiRepoKit.Spec;

public static class VerificationValidator
{
    private const string _evidencePrefix =
        "EVD-";

    private const string _verificationResultPrefix =
        "VER-";

    private const string _acceptanceCriterionPrefix =
        "AC-";

    private const string _planStepPrefix =
        "PLAN-STEP-";

    public static IReadOnlyList<SpecValidationError> Validate(
        IReadOnlyList<VerificationEvidence> evidence_,
        IReadOnlyList<VerificationResult> results_,
        WorkSpec workSpec_,
        ImplementationPlan implementationPlan_)
    {
        ArgumentNullException.ThrowIfNull(
            evidence_);

        ArgumentNullException.ThrowIfNull(
            results_);

        ArgumentNullException.ThrowIfNull(
            workSpec_);

        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        List<SpecValidationError> errors =
            [];

        HashSet<string> acceptanceCriterionIds =
            workSpec_
                .AcceptanceCriteria
                .Where(
                    criterion_ =>
                        IsKind(
                            criterion_.Id,
                            _acceptanceCriterionPrefix))
                .Select(
                    criterion_ =>
                        criterion_.Id.Value)
                .ToHashSet(
                    StringComparer.Ordinal);

        HashSet<string> planStepIds =
            implementationPlan_
                .Steps
                .Where(
                    step_ =>
                        IsKind(
                            step_.Id,
                            _planStepPrefix))
                .Select(
                    step_ =>
                        step_.Id.Value)
                .ToHashSet(
                    StringComparer.Ordinal);

        HashSet<string> evidenceIds =
            ValidateEvidence(
                evidence_,
                acceptanceCriterionIds,
                planStepIds,
                errors);

        ValidateResults(
            results_,
            acceptanceCriterionIds,
            evidenceIds,
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

    private static HashSet<string> ValidateEvidence(
        IReadOnlyList<VerificationEvidence> evidence_,
        HashSet<string> acceptanceCriterionIds_,
        HashSet<string> planStepIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> evidenceIds =
            new(
                StringComparer.Ordinal);

        foreach (VerificationEvidence evidence in
                 evidence_)
        {
            string evidenceId =
                evidence.Id.Value;

            if (!IsKind(
                    evidence.Id,
                    _evidencePrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidEntityKind,
                        SourceEntityId =
                            evidenceId,
                        Message =
                            $"Verification evidence ID '{evidenceId}' must use the EVD prefix."
                    });
            }
            else if (!evidenceIds.Add(
                         evidenceId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateEntityId,
                        SourceEntityId =
                            evidenceId,
                        Message =
                            $"Duplicate verification evidence ID '{evidenceId}'."
                    });
            }

            ValidateReferences(
                evidence.Id,
                evidence.AcceptanceCriterionIds,
                _acceptanceCriterionPrefix,
                "acceptance criterion",
                acceptanceCriterionIds_,
                errors_);

            ValidateReferences(
                evidence.Id,
                evidence.PlanStepIds,
                _planStepPrefix,
                "plan step",
                planStepIds_,
                errors_);
        }

        return evidenceIds;
    }

    private static void ValidateResults(
        IReadOnlyList<VerificationResult> results_,
        HashSet<string> acceptanceCriterionIds_,
        HashSet<string> evidenceIds_,
        List<SpecValidationError> errors_)
    {
        HashSet<string> resultIds =
            new(
                StringComparer.Ordinal);

        foreach (VerificationResult result in
                 results_)
        {
            string resultId =
                result.Id.Value;

            if (!IsKind(
                    result.Id,
                    _verificationResultPrefix))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidEntityKind,
                        SourceEntityId =
                            resultId,
                        Message =
                            $"Verification result ID '{resultId}' must use the VER prefix."
                    });
            }
            else if (!resultIds.Add(
                         resultId))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.DuplicateEntityId,
                        SourceEntityId =
                            resultId,
                        Message =
                            $"Duplicate verification result ID '{resultId}'."
                    });
            }

            ValidateSingleReference(
                result.Id,
                result.AcceptanceCriterionId,
                _acceptanceCriterionPrefix,
                "acceptance criterion",
                acceptanceCriterionIds_,
                errors_);

            ValidateReferences(
                result.Id,
                result.EvidenceIds,
                _evidencePrefix,
                "verification evidence",
                evidenceIds_,
                errors_);

            if (!Enum.IsDefined(
                    result.Status))
            {
                errors_.Add(
                    new SpecValidationError
                    {
                        Code =
                            SpecValidationErrorCodes.InvalidVerificationStatus,
                        SourceEntityId =
                            resultId,
                        Message =
                            $"Verification result '{resultId}' contains invalid status '{result.Status}'."
                    });
            }
        }
    }

    private static void ValidateSingleReference(
        StableEntityId sourceEntityId_,
        StableEntityId reference_,
        string expectedPrefix_,
        string targetLabel_,
        HashSet<string> knownTargetIds_,
        List<SpecValidationError> errors_)
    {
        string targetId =
            reference_.Value;

        if (!IsKind(
                reference_,
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
                        $"Entity '{sourceEntityId_.Value}' reference '{targetId}' must target a {targetLabel_}."
                });

            return;
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
                        $"Entity '{sourceEntityId_.Value}' references missing {targetLabel_} '{targetId}'."
                });
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
                            $"Entity '{sourceEntityId_.Value}' reference '{targetId}' must target a {targetLabel_}."
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
                            $"Entity '{sourceEntityId_.Value}' contains duplicate {targetLabel_} reference '{targetId}'."
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
                            $"Entity '{sourceEntityId_.Value}' references missing {targetLabel_} '{targetId}'."
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
