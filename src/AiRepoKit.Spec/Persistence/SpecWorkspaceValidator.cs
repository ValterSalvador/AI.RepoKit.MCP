namespace AiRepoKit.Spec.Persistence;

internal static class SpecWorkspaceValidator
{
    public static void ValidateForStore(
        RequirementSet requirementSet_)
    {
        ThrowIfInvalid(
            SpecArtifactKind.RequirementSet,
            RunValidator(
                SpecArtifactKind.RequirementSet,
                () =>
                    RequirementSetValidator.Validate(
                        requirementSet_)));
    }

    public static void ValidateForStore(
        WorkSpec workSpec_,
        RequirementSet requirementSet_)
    {
        ThrowIfInvalid(
            SpecArtifactKind.WorkSpec,
            RunValidator(
                SpecArtifactKind.WorkSpec,
                () =>
                    WorkSpecValidator.Validate(
                        workSpec_,
                        requirementSet_)));
    }

    public static void ValidateForStore(
        ImplementationPlan implementationPlan_,
        WorkSpec workSpec_,
        RequirementSet requirementSet_)
    {
        ThrowIfInvalid(
            SpecArtifactKind.ImplementationPlan,
            RunValidator(
                SpecArtifactKind.ImplementationPlan,
                () =>
                    ImplementationPlanValidator.Validate(
                        implementationPlan_,
                        workSpec_,
                        requirementSet_)));
    }

    public static SpecWorkspaceSnapshot Validate(
        RequirementSet? requirementSet_,
        WorkSpec? workSpec_,
        ImplementationPlan? implementationPlan_)
    {
        ValidateDependencyPresence(
            requirementSet_ is not null,
            workSpec_ is not null,
            implementationPlan_ is not null);

        if (requirementSet_ is null)
        {
            return new SpecWorkspaceSnapshot(
                null,
                null,
                null,
                false,
                false);
        }

        ThrowIfInvalid(
            SpecArtifactKind.RequirementSet,
            RunValidator(
                SpecArtifactKind.RequirementSet,
                () =>
                    RequirementSetValidator.Validate(
                        requirementSet_)));

        bool isWorkSpecStale =
            workSpec_ is not null &&
            workSpec_.RequirementSetRevision !=
            requirementSet_.Revision;

        if (workSpec_ is not null)
        {
            ThrowIfInvalid(
                SpecArtifactKind.WorkSpec,
                FilterWorkSpecStalenessErrors(
                    RunValidator(
                        SpecArtifactKind.WorkSpec,
                        () =>
                            WorkSpecValidator.Validate(
                                workSpec_,
                                requirementSet_)),
                    isWorkSpecStale));
        }

        bool isImplementationPlanRevisionStale =
            implementationPlan_ is not null &&
            implementationPlan_.WorkSpecRevision !=
            workSpec_!.Revision;
        bool isImplementationPlanStale =
            implementationPlan_ is not null &&
            (isWorkSpecStale ||
             isImplementationPlanRevisionStale);

        if (implementationPlan_ is not null)
        {
            ThrowIfInvalid(
                SpecArtifactKind.ImplementationPlan,
                FilterImplementationPlanStalenessErrors(
                    RunValidator(
                        SpecArtifactKind.ImplementationPlan,
                        () =>
                            ImplementationPlanValidator.Validate(
                                implementationPlan_,
                                workSpec_!,
                                requirementSet_)),
                    isWorkSpecStale,
                    isImplementationPlanRevisionStale));
        }

        return new SpecWorkspaceSnapshot(
            requirementSet_,
            workSpec_,
            implementationPlan_,
            isWorkSpecStale,
            isImplementationPlanStale);
    }

    public static void ValidateDependencyPresence(
        bool requirementSetExists_,
        bool workSpecExists_,
        bool implementationPlanExists_)
    {
        if (workSpecExists_ &&
            !requirementSetExists_)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "The canonical WorkSpec requires a canonical RequirementSet.",
                SpecArtifactKind.WorkSpec);
        }

        if (implementationPlanExists_ &&
            !workSpecExists_)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "The canonical ImplementationPlan requires a canonical WorkSpec.",
                SpecArtifactKind.ImplementationPlan);
        }

        if (implementationPlanExists_ &&
            !requirementSetExists_)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "The canonical ImplementationPlan requires a canonical RequirementSet.",
                SpecArtifactKind.ImplementationPlan);
        }
    }

    private static IReadOnlyList<SpecValidationError> FilterWorkSpecStalenessErrors(
        IReadOnlyList<SpecValidationError> errors_,
        bool isWorkSpecStale_)
    {
        if (!isWorkSpecStale_)
        {
            return errors_;
        }

        return errors_
            .Where(
                error_ =>
                    error_.Code !=
                    SpecValidationErrorCodes.RevisionMismatch &&
                    error_.Code !=
                    SpecValidationErrorCodes.DanglingReference)
            .ToArray();
    }

    private static IReadOnlyList<SpecValidationError> FilterImplementationPlanStalenessErrors(
        IReadOnlyList<SpecValidationError> errors_,
        bool isWorkSpecStale_,
        bool isImplementationPlanRevisionStale_)
    {
        if (!isWorkSpecStale_ &&
            !isImplementationPlanRevisionStale_)
        {
            return errors_;
        }

        return errors_
            .Where(
                error_ =>
                    !IsExpectedImplementationPlanStalenessError(
                        error_,
                        isWorkSpecStale_,
                        isImplementationPlanRevisionStale_))
            .ToArray();
    }

    private static bool IsExpectedImplementationPlanStalenessError(
        SpecValidationError error_,
        bool isWorkSpecStale_,
        bool isImplementationPlanRevisionStale_)
    {
        if (error_.Code ==
            SpecValidationErrorCodes.RevisionMismatch)
        {
            return isImplementationPlanRevisionStale_;
        }

        if (error_.Code !=
            SpecValidationErrorCodes.DanglingReference)
        {
            return false;
        }

        if (error_.TargetEntityId.StartsWith(
                "AC-",
                StringComparison.Ordinal))
        {
            return isImplementationPlanRevisionStale_;
        }

        return error_.TargetEntityId.StartsWith(
                   "REQ-",
                   StringComparison.Ordinal) &&
               (isWorkSpecStale_ ||
                isImplementationPlanRevisionStale_);
    }

    private static IReadOnlyList<SpecValidationError> RunValidator(
        SpecArtifactKind artifactKind_,
        Func<IReadOnlyList<SpecValidationError>> validator_)
    {
        try
        {
            return validator_();
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            NullReferenceException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                $"The canonical {artifactKind_} failed validation.",
                artifactKind_,
                innerException_: exception);
        }
    }

    private static void ThrowIfInvalid(
        SpecArtifactKind artifactKind_,
        IReadOnlyList<SpecValidationError> errors_)
    {
        if (errors_.Count == 0)
        {
            return;
        }

        SpecValidationError[] orderedErrors =
            errors_
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

        throw new SpecPersistenceException(
            SpecPersistenceException.ValidationFailed,
            $"The canonical {artifactKind_} failed validation.",
            artifactKind_,
            orderedErrors);
    }
}
