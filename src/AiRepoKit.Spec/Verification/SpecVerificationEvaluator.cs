using System.Globalization;

namespace AiRepoKit.Spec.Verification;

public static class SpecVerificationEvaluator
{
    public static SpecVerificationReport Evaluate(
        string specId_,
        ArtifactRevision requirementSetRevision_,
        ArtifactRevision workSpecRevision_,
        ArtifactRevision implementationPlanRevision_,
        IReadOnlyList<VerificationEvidence> evidence_,
        IReadOnlyList<SpecVerificationEvidenceObservation> observations_,
        WorkSpec workSpec_)
    {
        ArgumentNullException.ThrowIfNull(evidence_);
        ArgumentNullException.ThrowIfNull(observations_);
        ArgumentNullException.ThrowIfNull(workSpec_);

        // Build observation lookup by EvidenceId (ordinal)
        Dictionary<string, SpecVerificationEvidenceObservation> obsById =
            new(StringComparer.Ordinal);
        foreach (SpecVerificationEvidenceObservation obs in observations_)
        {
            obsById[obs.EvidenceId.Value] = obs;
        }

        // Sort ACs by Id ordinal
        IReadOnlyList<AcceptanceCriterion> sortedCriteria = workSpec_
            .AcceptanceCriteria
            .OrderBy(ac_ => ac_.Id.Value, StringComparer.Ordinal)
            .ToArray();

        List<VerificationResult> results = [];
        int seq = 1;

        foreach (AcceptanceCriterion criterion in sortedCriteria)
        {
            string resultIdValue = "VER-" + seq.ToString("D3", CultureInfo.InvariantCulture);
            seq++;

            // Collect evidence that references this AC
            IReadOnlyList<VerificationEvidence> relevantEvidence = evidence_
                .Where(evd_ => evd_.AcceptanceCriterionIds.Any(
                    acId_ => string.Equals(acId_.Value, criterion.Id.Value, StringComparison.Ordinal)))
                .ToArray();

            // Collect observations for relevant evidence
            List<SpecVerificationEvidenceObservation> relevantObs = [];
            foreach (VerificationEvidence evd in relevantEvidence)
            {
                if (obsById.TryGetValue(evd.Id.Value, out SpecVerificationEvidenceObservation? obs))
                {
                    relevantObs.Add(obs);
                }
            }

            // Aggregate
            VerificationStatus status;
            string summary;
            bool anyContradicting = relevantObs.Any(o_ => o_.Disposition == SpecVerificationEvidenceDisposition.Contradicting);
            bool anySupporting = relevantObs.Any(o_ => o_.Disposition == SpecVerificationEvidenceDisposition.Supporting);

            if (anyContradicting)
            {
                status = VerificationStatus.Fail;
                summary = "Acceptance criterion has contradicting evidence.";
            }
            else if (anySupporting)
            {
                status = VerificationStatus.Pass;
                summary = "Acceptance criterion has recognized supporting evidence.";
            }
            else
            {
                status = VerificationStatus.NotVerified;
                summary = relevantObs.Count == 0
                    ? "No evidence bound to this acceptance criterion."
                    : "All bound evidence is inconclusive. No recognized supporting evidence.";
            }

            // Deterministic sorted evidence IDs
            IReadOnlyList<StableEntityId> evidenceIds = relevantEvidence
                .Select(evd_ => evd_.Id)
                .OrderBy(id_ => id_.Value, StringComparer.Ordinal)
                .ToArray();

            results.Add(new VerificationResult
            {
                Id = new StableEntityId(resultIdValue),
                AcceptanceCriterionId = criterion.Id,
                Status = status,
                EvidenceIds = evidenceIds,
                Summary = summary
            });
        }

        // Overall status
        VerificationStatus overallStatus;
        if (results.Any(r_ => r_.Status == VerificationStatus.Fail))
        {
            overallStatus = VerificationStatus.Fail;
        }
        else if (results.Any(r_ => r_.Status == VerificationStatus.NotVerified))
        {
            overallStatus = VerificationStatus.NotVerified;
        }
        else
        {
            overallStatus = VerificationStatus.Pass;
        }

        // Observations sorted by EvidenceId ordinal
        IReadOnlyList<SpecVerificationEvidenceObservation> sortedObservations = observations_
            .OrderBy(o_ => o_.EvidenceId.Value, StringComparer.Ordinal)
            .ToArray();

        return new SpecVerificationReport
        {
            SpecId = specId_,
            RequirementSetRevision = requirementSetRevision_,
            WorkSpecRevision = workSpecRevision_,
            ImplementationPlanRevision = implementationPlanRevision_,
            OverallStatus = overallStatus,
            Results = results.ToArray(),
            Observations = sortedObservations
        };
    }
}
