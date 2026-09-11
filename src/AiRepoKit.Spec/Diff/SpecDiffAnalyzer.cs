using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Spec.Diff;

public static class SpecDiffAnalyzer
{
    public static SpecDiffResult Analyze(
        SpecId specId_,
        SpecWorkspaceSnapshot currentSnapshot_,
        SpecApprovalLedger? ledger_,
        SpecArtifactKind artifactKind_,
        object candidateArtifact_)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot_);
        ArgumentNullException.ThrowIfNull(candidateArtifact_);

        return artifactKind_ switch
        {
            SpecArtifactKind.RequirementSet => AnalyzeRequirementSet(
                specId_,
                currentSnapshot_,
                ledger_,
                (RequirementSet)candidateArtifact_),
            SpecArtifactKind.WorkSpec => AnalyzeWorkSpec(
                specId_,
                currentSnapshot_,
                ledger_,
                (WorkSpec)candidateArtifact_),
            SpecArtifactKind.ImplementationPlan => AnalyzeImplementationPlan(
                specId_,
                currentSnapshot_,
                ledger_,
                (ImplementationPlan)candidateArtifact_),
            _ => throw new ArgumentOutOfRangeException(
                nameof(artifactKind_),
                artifactKind_,
                "Unsupported artifact kind for spec diff.")
        };
    }

    public static SpecDiffResult AnalyzeRequirementSet(
        SpecId specId_,
        SpecWorkspaceSnapshot currentSnapshot_,
        SpecApprovalLedger? ledger_,
        RequirementSet candidate_)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot_);
        ArgumentNullException.ThrowIfNull(candidate_);

        SpecWorkspaceValidator.ValidateForStore(candidate_);

        RequirementSet? current = currentSnapshot_.RequirementSet;
        ArtifactRevision? currentRevision = current?.Revision;

        string candidateDigest = SpecSemanticDigest.Compute(candidate_);
        string? currentDigest = current is not null ? SpecSemanticDigest.Compute(current) : null;
        bool semanticChanged = currentDigest is null || !string.Equals(currentDigest, candidateDigest, StringComparison.Ordinal);
        bool orderingChanged = false;

        ArtifactRevision proposedRevision = currentRevision is null
            ? new ArtifactRevision(1)
            : (semanticChanged ? new ArtifactRevision(checked(currentRevision.Value.Value + 1)) : currentRevision.Value);

        List<SpecEntityChange> entityChanges = [];

        // RequirementInput entities
        Dictionary<string, RequirementInput> currentInputs = (current?.Inputs ?? [])
            .ToDictionary(input_ => input_.Id.Value, StringComparer.Ordinal);
        Dictionary<string, RequirementInput> candidateInputs = candidate_.Inputs
            .ToDictionary(input_ => input_.Id.Value, StringComparer.Ordinal);

        List<string> allInputIds = currentInputs.Keys
            .Union(candidateInputs.Keys, StringComparer.Ordinal)
            .OrderBy(id_ => id_, StringComparer.Ordinal)
            .ToList();

        foreach (string id in allInputIds)
        {
            bool inCurrent = currentInputs.TryGetValue(id, out RequirementInput? curInput);
            bool inCandidate = candidateInputs.TryGetValue(id, out RequirementInput? candInput);

            if (inCandidate && !inCurrent)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.RequirementInput,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Added
                });
            }
            else if (inCurrent && !inCandidate)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.RequirementInput,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Removed
                });
            }
            else
            {
                bool isSame = string.Equals(curInput!.Text, candInput!.Text, StringComparison.Ordinal);
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.RequirementInput,
                    EntityId = id,
                    ChangeKind = isSame ? SpecChangeKind.Unchanged : SpecChangeKind.Modified
                });
            }
        }

        // Requirement entities
        Dictionary<string, Requirement> currentReqs = (current?.Requirements ?? [])
            .ToDictionary(req_ => req_.Id.Value, StringComparer.Ordinal);
        Dictionary<string, Requirement> candidateReqs = candidate_.Requirements
            .ToDictionary(req_ => req_.Id.Value, StringComparer.Ordinal);

        List<string> allReqIds = currentReqs.Keys
            .Union(candidateReqs.Keys, StringComparer.Ordinal)
            .OrderBy(id_ => id_, StringComparer.Ordinal)
            .ToList();

        foreach (string id in allReqIds)
        {
            bool inCurrent = currentReqs.TryGetValue(id, out Requirement? curReq);
            bool inCandidate = candidateReqs.TryGetValue(id, out Requirement? candReq);

            if (inCandidate && !inCurrent)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.Requirement,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Added
                });
            }
            else if (inCurrent && !inCandidate)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.Requirement,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Removed
                });
            }
            else
            {
                bool sameStatement = string.Equals(curReq!.Statement, candReq!.Statement, StringComparison.Ordinal);
                string[] curSrc = curReq.SourceInputIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                string[] candSrc = candReq.SourceInputIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                bool sameReferences = curSrc.SequenceEqual(candSrc, StringComparer.Ordinal);

                bool isSame = sameStatement && sameReferences;
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.Requirement,
                    EntityId = id,
                    ChangeKind = isSame ? SpecChangeKind.Unchanged : SpecChangeKind.Modified
                });
            }
        }

        // Hypothetical proposed snapshot
        RequirementSet proposedRS = candidate_ with { Revision = proposedRevision };
        SpecWorkspaceSnapshot proposedSnapshot = SpecWorkspaceValidator.Validate(
            proposedRS,
            currentSnapshot_.WorkSpec,
            currentSnapshot_.ImplementationPlan);

        // Approval impacts
        IReadOnlyList<SpecArtifactApprovalStatus> currentStatuses = SpecApprovalStatusEvaluator.Evaluate(currentSnapshot_, ledger_);
        IReadOnlyList<SpecArtifactApprovalStatus> proposedStatuses = SpecApprovalStatusEvaluator.Evaluate(proposedSnapshot, ledger_);

        List<SpecApprovalImpact> approvalImpacts = [];
        AddApprovalImpact(approvalImpacts, SpecArtifactKind.RequirementSet, currentStatuses, proposedStatuses);
        if (proposedSnapshot.WorkSpec is not null)
        {
            AddApprovalImpact(approvalImpacts, SpecArtifactKind.WorkSpec, currentStatuses, proposedStatuses);
        }
        if (proposedSnapshot.ImplementationPlan is not null)
        {
            AddApprovalImpact(approvalImpacts, SpecArtifactKind.ImplementationPlan, currentStatuses, proposedStatuses);
        }

        // Downstream reference impacts
        HashSet<string> changedReqIds = entityChanges
            .Where(c_ => c_.EntityKind == SpecDiffEntityKind.Requirement && c_.ChangeKind != SpecChangeKind.Unchanged)
            .Select(c_ => c_.EntityId)
            .ToHashSet(StringComparer.Ordinal);

        List<SpecReferenceImpact> referenceImpacts = [];

        if (currentSnapshot_.WorkSpec is not null)
        {
            foreach (Constraint constraint in currentSnapshot_.WorkSpec.Constraints)
            {
                string[] matching = constraint.RequirementIds
                    .Select(id_ => id_.Value)
                    .Where(changedReqIds.Contains)
                    .OrderBy(x_ => x_, StringComparer.Ordinal)
                    .ToArray();

                if (matching.Length > 0)
                {
                    referenceImpacts.Add(new SpecReferenceImpact
                    {
                        ArtifactKind = SpecArtifactKind.WorkSpec,
                        EntityKind = SpecDiffEntityKind.Constraint,
                        EntityId = constraint.Id.Value,
                        ReferencedChangedIds = matching
                    });
                }
            }

            foreach (AcceptanceCriterion ac in currentSnapshot_.WorkSpec.AcceptanceCriteria)
            {
                string[] matching = ac.RequirementIds
                    .Select(id_ => id_.Value)
                    .Where(changedReqIds.Contains)
                    .OrderBy(x_ => x_, StringComparer.Ordinal)
                    .ToArray();

                if (matching.Length > 0)
                {
                    referenceImpacts.Add(new SpecReferenceImpact
                    {
                        ArtifactKind = SpecArtifactKind.WorkSpec,
                        EntityKind = SpecDiffEntityKind.AcceptanceCriterion,
                        EntityId = ac.Id.Value,
                        ReferencedChangedIds = matching
                    });
                }
            }
        }

        if (currentSnapshot_.ImplementationPlan is not null)
        {
            foreach (PlanStep step in currentSnapshot_.ImplementationPlan.Steps)
            {
                string[] matching = step.RequirementIds
                    .Select(id_ => id_.Value)
                    .Where(changedReqIds.Contains)
                    .OrderBy(x_ => x_, StringComparer.Ordinal)
                    .ToArray();

                if (matching.Length > 0)
                {
                    referenceImpacts.Add(new SpecReferenceImpact
                    {
                        ArtifactKind = SpecArtifactKind.ImplementationPlan,
                        EntityKind = SpecDiffEntityKind.PlanStep,
                        EntityId = step.Id.Value,
                        ReferencedChangedIds = matching
                    });
                }
            }
        }

        referenceImpacts = referenceImpacts
            .OrderBy(r_ => r_.ArtifactKind == SpecArtifactKind.WorkSpec ? 0 : 1)
            .ThenBy(r_ => r_.EntityKind switch
            {
                SpecDiffEntityKind.Constraint => 0,
                SpecDiffEntityKind.AcceptanceCriterion => 1,
                SpecDiffEntityKind.PlanStep => 2,
                _ => 99
            })
            .ThenBy(r_ => r_.EntityId, StringComparer.Ordinal)
            .ToList();

        // Derived artifact impacts
        bool specContextAffected = semanticChanged;
        bool checklistAffected = currentSnapshot_.ImplementationPlan is not null &&
            (proposedSnapshot.IsImplementationPlanStale != currentSnapshot_.IsImplementationPlanStale ||
             proposedStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan)?.Status !=
             currentStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan)?.Status);

        List<SpecDerivedArtifactImpact> derivedImpacts =
        [
            new SpecDerivedArtifactImpact
            {
                ArtifactKind = SpecDerivedArtifactKind.SpecContext,
                Affected = specContextAffected
            },
            new SpecDerivedArtifactImpact
            {
                ArtifactKind = SpecDerivedArtifactKind.ImplementationChecklist,
                Affected = checklistAffected
            }
        ];

        return new SpecDiffResult
        {
            SpecId = specId_.Value,
            ArtifactKind = SpecArtifactKind.RequirementSet,
            ArtifactIdentity = candidate_.ArtifactIdentity,
            CurrentRevision = currentRevision,
            ProposedRevision = proposedRevision,
            CurrentSemanticDigest = currentDigest,
            CandidateSemanticDigest = candidateDigest,
            SemanticChanged = semanticChanged,
            OrderingChanged = orderingChanged,
            EntityChanges = entityChanges,
            ReferenceImpacts = referenceImpacts,
            ApprovalImpacts = approvalImpacts,
            DerivedArtifactImpacts = derivedImpacts
        };
    }

    public static SpecDiffResult AnalyzeWorkSpec(
        SpecId specId_,
        SpecWorkspaceSnapshot currentSnapshot_,
        SpecApprovalLedger? ledger_,
        WorkSpec candidate_)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot_);
        ArgumentNullException.ThrowIfNull(candidate_);

        RequirementSet requirementSet = currentSnapshot_.RequirementSet ??
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "A WorkSpec cannot be diffed without a canonical RequirementSet.",
                SpecArtifactKind.WorkSpec);

        SpecWorkspaceValidator.ValidateForStore(candidate_, requirementSet);

        WorkSpec? current = currentSnapshot_.WorkSpec;
        ArtifactRevision? currentRevision = current?.Revision;

        string candidateDigest = SpecSemanticDigest.Compute(candidate_);
        string? currentDigest = current is not null ? SpecSemanticDigest.Compute(current) : null;
        bool semanticChanged = currentDigest is null || !string.Equals(currentDigest, candidateDigest, StringComparison.Ordinal);
        bool orderingChanged = false;

        ArtifactRevision proposedRevision = currentRevision is null
            ? new ArtifactRevision(1)
            : (semanticChanged ? new ArtifactRevision(checked(currentRevision.Value.Value + 1)) : currentRevision.Value);

        List<SpecEntityChange> entityChanges = [];

        // Constraint entities
        Dictionary<string, Constraint> currentConstraints = (current?.Constraints ?? [])
            .ToDictionary(c_ => c_.Id.Value, StringComparer.Ordinal);
        Dictionary<string, Constraint> candidateConstraints = candidate_.Constraints
            .ToDictionary(c_ => c_.Id.Value, StringComparer.Ordinal);

        List<string> allConIds = currentConstraints.Keys
            .Union(candidateConstraints.Keys, StringComparer.Ordinal)
            .OrderBy(id_ => id_, StringComparer.Ordinal)
            .ToList();

        foreach (string id in allConIds)
        {
            bool inCurrent = currentConstraints.TryGetValue(id, out Constraint? curCon);
            bool inCandidate = candidateConstraints.TryGetValue(id, out Constraint? candCon);

            if (inCandidate && !inCurrent)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.Constraint,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Added
                });
            }
            else if (inCurrent && !inCandidate)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.Constraint,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Removed
                });
            }
            else
            {
                bool sameStatement = string.Equals(curCon!.Statement, candCon!.Statement, StringComparison.Ordinal);
                string[] curReqs = curCon.RequirementIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                string[] candReqs = candCon.RequirementIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                bool sameReferences = curReqs.SequenceEqual(candReqs, StringComparer.Ordinal);

                bool isSame = sameStatement && sameReferences;
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.Constraint,
                    EntityId = id,
                    ChangeKind = isSame ? SpecChangeKind.Unchanged : SpecChangeKind.Modified
                });
            }
        }

        // AcceptanceCriterion entities
        Dictionary<string, AcceptanceCriterion> currentAcs = (current?.AcceptanceCriteria ?? [])
            .ToDictionary(ac_ => ac_.Id.Value, StringComparer.Ordinal);
        Dictionary<string, AcceptanceCriterion> candidateAcs = candidate_.AcceptanceCriteria
            .ToDictionary(ac_ => ac_.Id.Value, StringComparer.Ordinal);

        List<string> allAcIds = currentAcs.Keys
            .Union(candidateAcs.Keys, StringComparer.Ordinal)
            .OrderBy(id_ => id_, StringComparer.Ordinal)
            .ToList();

        foreach (string id in allAcIds)
        {
            bool inCurrent = currentAcs.TryGetValue(id, out AcceptanceCriterion? curAc);
            bool inCandidate = candidateAcs.TryGetValue(id, out AcceptanceCriterion? candAc);

            if (inCandidate && !inCurrent)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.AcceptanceCriterion,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Added
                });
            }
            else if (inCurrent && !inCandidate)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.AcceptanceCriterion,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Removed
                });
            }
            else
            {
                bool sameStatement = string.Equals(curAc!.Statement, candAc!.Statement, StringComparison.Ordinal);
                string[] curReqs = curAc.RequirementIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                string[] candReqs = candAc.RequirementIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                bool sameReferences = curReqs.SequenceEqual(candReqs, StringComparer.Ordinal);

                bool isSame = sameStatement && sameReferences;
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.AcceptanceCriterion,
                    EntityId = id,
                    ChangeKind = isSame ? SpecChangeKind.Unchanged : SpecChangeKind.Modified
                });
            }
        }

        // Hypothetical proposed snapshot
        WorkSpec proposedWS = candidate_ with { Revision = proposedRevision };
        SpecWorkspaceSnapshot proposedSnapshot = SpecWorkspaceValidator.Validate(
            currentSnapshot_.RequirementSet,
            proposedWS,
            currentSnapshot_.ImplementationPlan);

        // Approval impacts
        IReadOnlyList<SpecArtifactApprovalStatus> currentStatuses = SpecApprovalStatusEvaluator.Evaluate(currentSnapshot_, ledger_);
        IReadOnlyList<SpecArtifactApprovalStatus> proposedStatuses = SpecApprovalStatusEvaluator.Evaluate(proposedSnapshot, ledger_);

        List<SpecApprovalImpact> approvalImpacts = [];
        AddApprovalImpact(approvalImpacts, SpecArtifactKind.RequirementSet, currentStatuses, proposedStatuses);
        AddApprovalImpact(approvalImpacts, SpecArtifactKind.WorkSpec, currentStatuses, proposedStatuses);
        if (proposedSnapshot.ImplementationPlan is not null)
        {
            AddApprovalImpact(approvalImpacts, SpecArtifactKind.ImplementationPlan, currentStatuses, proposedStatuses);
        }

        // Downstream reference impacts: changed AC IDs referenced by ImplementationPlan steps
        HashSet<string> changedAcIds = entityChanges
            .Where(c_ => c_.EntityKind == SpecDiffEntityKind.AcceptanceCriterion && c_.ChangeKind != SpecChangeKind.Unchanged)
            .Select(c_ => c_.EntityId)
            .ToHashSet(StringComparer.Ordinal);

        List<SpecReferenceImpact> referenceImpacts = [];

        if (currentSnapshot_.ImplementationPlan is not null)
        {
            foreach (PlanStep step in currentSnapshot_.ImplementationPlan.Steps)
            {
                string[] matching = step.AcceptanceCriterionIds
                    .Select(id_ => id_.Value)
                    .Where(changedAcIds.Contains)
                    .OrderBy(x_ => x_, StringComparer.Ordinal)
                    .ToArray();

                if (matching.Length > 0)
                {
                    referenceImpacts.Add(new SpecReferenceImpact
                    {
                        ArtifactKind = SpecArtifactKind.ImplementationPlan,
                        EntityKind = SpecDiffEntityKind.PlanStep,
                        EntityId = step.Id.Value,
                        ReferencedChangedIds = matching
                    });
                }
            }
        }

        referenceImpacts = referenceImpacts
            .OrderBy(r_ => r_.EntityId, StringComparer.Ordinal)
            .ToList();

        // Derived artifact impacts
        bool specContextAffected = semanticChanged;
        bool checklistAffected = currentSnapshot_.ImplementationPlan is not null &&
            (proposedSnapshot.IsImplementationPlanStale != currentSnapshot_.IsImplementationPlanStale ||
             proposedStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan)?.Status !=
             currentStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan)?.Status);

        List<SpecDerivedArtifactImpact> derivedImpacts =
        [
            new SpecDerivedArtifactImpact
            {
                ArtifactKind = SpecDerivedArtifactKind.SpecContext,
                Affected = specContextAffected
            },
            new SpecDerivedArtifactImpact
            {
                ArtifactKind = SpecDerivedArtifactKind.ImplementationChecklist,
                Affected = checklistAffected
            }
        ];

        return new SpecDiffResult
        {
            SpecId = specId_.Value,
            ArtifactKind = SpecArtifactKind.WorkSpec,
            ArtifactIdentity = candidate_.ArtifactIdentity,
            CurrentRevision = currentRevision,
            ProposedRevision = proposedRevision,
            CurrentSemanticDigest = currentDigest,
            CandidateSemanticDigest = candidateDigest,
            SemanticChanged = semanticChanged,
            OrderingChanged = orderingChanged,
            EntityChanges = entityChanges,
            ReferenceImpacts = referenceImpacts,
            ApprovalImpacts = approvalImpacts,
            DerivedArtifactImpacts = derivedImpacts
        };
    }

    public static SpecDiffResult AnalyzeImplementationPlan(
        SpecId specId_,
        SpecWorkspaceSnapshot currentSnapshot_,
        SpecApprovalLedger? ledger_,
        ImplementationPlan candidate_)
    {
        ArgumentNullException.ThrowIfNull(currentSnapshot_);
        ArgumentNullException.ThrowIfNull(candidate_);

        if (currentSnapshot_.RequirementSet is null || currentSnapshot_.WorkSpec is null)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "An ImplementationPlan cannot be diffed without canonical RequirementSet and WorkSpec dependencies.",
                SpecArtifactKind.ImplementationPlan);
        }

        if (currentSnapshot_.IsWorkSpecStale)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.StaleDependency,
                "An ImplementationPlan cannot be diffed against a stale canonical WorkSpec.",
                SpecArtifactKind.ImplementationPlan);
        }

        SpecWorkspaceValidator.ValidateForStore(
            candidate_,
            currentSnapshot_.WorkSpec,
            currentSnapshot_.RequirementSet);

        ImplementationPlan? current = currentSnapshot_.ImplementationPlan;
        ArtifactRevision? currentRevision = current?.Revision;

        string candidateDigest = SpecSemanticDigest.Compute(candidate_);
        string? currentDigest = current is not null ? SpecSemanticDigest.Compute(current) : null;
        bool semanticChanged = currentDigest is null || !string.Equals(currentDigest, candidateDigest, StringComparison.Ordinal);

        bool orderingChanged = false;
        if (current is not null)
        {
            List<string> currentIds = current.Steps.Select(s_ => s_.Id.Value).ToList();
            List<string> candidateIds = candidate_.Steps.Select(s_ => s_.Id.Value).ToList();
            List<string> currentCommon = currentIds.Where(candidateIds.Contains).ToList();
            List<string> candidateCommon = candidateIds.Where(currentIds.Contains).ToList();
            orderingChanged = !currentCommon.SequenceEqual(candidateCommon, StringComparer.Ordinal);
        }

        ArtifactRevision proposedRevision = currentRevision is null
            ? new ArtifactRevision(1)
            : (semanticChanged ? new ArtifactRevision(checked(currentRevision.Value.Value + 1)) : currentRevision.Value);

        List<SpecEntityChange> entityChanges = [];

        // PlanStep entities
        Dictionary<string, PlanStep> currentSteps = (current?.Steps ?? [])
            .ToDictionary(step_ => step_.Id.Value, StringComparer.Ordinal);
        Dictionary<string, PlanStep> candidateSteps = candidate_.Steps
            .ToDictionary(step_ => step_.Id.Value, StringComparer.Ordinal);

        List<string> allStepIds = currentSteps.Keys
            .Union(candidateSteps.Keys, StringComparer.Ordinal)
            .OrderBy(id_ => id_, StringComparer.Ordinal)
            .ToList();

        foreach (string id in allStepIds)
        {
            bool inCurrent = currentSteps.TryGetValue(id, out PlanStep? curStep);
            bool inCandidate = candidateSteps.TryGetValue(id, out PlanStep? candStep);

            if (inCandidate && !inCurrent)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.PlanStep,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Added
                });
            }
            else if (inCurrent && !inCandidate)
            {
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.PlanStep,
                    EntityId = id,
                    ChangeKind = SpecChangeKind.Removed
                });
            }
            else
            {
                bool sameStatement = string.Equals(curStep!.Statement, candStep!.Statement, StringComparison.Ordinal);
                string[] curReqs = curStep.RequirementIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                string[] candReqs = candStep.RequirementIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                bool sameReqs = curReqs.SequenceEqual(candReqs, StringComparer.Ordinal);

                string[] curAcs = curStep.AcceptanceCriterionIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                string[] candAcs = candStep.AcceptanceCriterionIds.Select(x_ => x_.Value).OrderBy(x_ => x_, StringComparer.Ordinal).ToArray();
                bool sameAcs = curAcs.SequenceEqual(candAcs, StringComparer.Ordinal);

                bool isSame = sameStatement && sameReqs && sameAcs;
                entityChanges.Add(new SpecEntityChange
                {
                    EntityKind = SpecDiffEntityKind.PlanStep,
                    EntityId = id,
                    ChangeKind = isSame ? SpecChangeKind.Unchanged : SpecChangeKind.Modified
                });
            }
        }

        // Hypothetical proposed snapshot
        ImplementationPlan proposedPlan = candidate_ with { Revision = proposedRevision };
        SpecWorkspaceSnapshot proposedSnapshot = SpecWorkspaceValidator.Validate(
            currentSnapshot_.RequirementSet,
            currentSnapshot_.WorkSpec,
            proposedPlan);

        // Approval impacts
        IReadOnlyList<SpecArtifactApprovalStatus> currentStatuses = SpecApprovalStatusEvaluator.Evaluate(currentSnapshot_, ledger_);
        IReadOnlyList<SpecArtifactApprovalStatus> proposedStatuses = SpecApprovalStatusEvaluator.Evaluate(proposedSnapshot, ledger_);

        List<SpecApprovalImpact> approvalImpacts = [];
        AddApprovalImpact(approvalImpacts, SpecArtifactKind.RequirementSet, currentStatuses, proposedStatuses);
        AddApprovalImpact(approvalImpacts, SpecArtifactKind.WorkSpec, currentStatuses, proposedStatuses);
        AddApprovalImpact(approvalImpacts, SpecArtifactKind.ImplementationPlan, currentStatuses, proposedStatuses);

        // Downstream reference impacts: empty for Plan
        IReadOnlyList<SpecReferenceImpact> referenceImpacts = Array.Empty<SpecReferenceImpact>();

        // Derived artifact impacts: SpecContext unaffected by Plan, ImplementationChecklist affected by Plan semantics change
        List<SpecDerivedArtifactImpact> derivedImpacts =
        [
            new SpecDerivedArtifactImpact
            {
                ArtifactKind = SpecDerivedArtifactKind.SpecContext,
                Affected = false
            },
            new SpecDerivedArtifactImpact
            {
                ArtifactKind = SpecDerivedArtifactKind.ImplementationChecklist,
                Affected = semanticChanged
            }
        ];

        return new SpecDiffResult
        {
            SpecId = specId_.Value,
            ArtifactKind = SpecArtifactKind.ImplementationPlan,
            ArtifactIdentity = candidate_.ArtifactIdentity,
            CurrentRevision = currentRevision,
            ProposedRevision = proposedRevision,
            CurrentSemanticDigest = currentDigest,
            CandidateSemanticDigest = candidateDigest,
            SemanticChanged = semanticChanged,
            OrderingChanged = orderingChanged,
            EntityChanges = entityChanges,
            ReferenceImpacts = referenceImpacts,
            ApprovalImpacts = approvalImpacts,
            DerivedArtifactImpacts = derivedImpacts
        };
    }

    private static void AddApprovalImpact(
        List<SpecApprovalImpact> impacts_,
        SpecArtifactKind kind_,
        IReadOnlyList<SpecArtifactApprovalStatus> currentStatuses_,
        IReadOnlyList<SpecArtifactApprovalStatus> proposedStatuses_)
    {
        SpecApprovalStatus currentStatus = currentStatuses_
            .FirstOrDefault(s_ => s_.ArtifactKind == kind_)?.Status ?? SpecApprovalStatus.NotApproved;
        SpecApprovalStatus proposedStatus = proposedStatuses_
            .FirstOrDefault(s_ => s_.ArtifactKind == kind_)?.Status ?? SpecApprovalStatus.NotApproved;

        impacts_.Add(new SpecApprovalImpact
        {
            ArtifactKind = kind_,
            CurrentStatus = currentStatus,
            ProposedStatus = proposedStatus,
            Affected = currentStatus != proposedStatus
        });
    }
}
