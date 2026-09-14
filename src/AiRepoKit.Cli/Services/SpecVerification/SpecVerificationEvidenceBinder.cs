using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Verification;

namespace AiRepoKit.Cli.Services.SpecVerification;

/// <summary>
/// Binds SpecVerificationEvidenceBinding records to RepositoryEvidence and produces
/// SpecVerificationEvidenceObservation records. Does NOT write any files.
/// </summary>
public sealed class SpecVerificationEvidenceBinder
{
    private static readonly IReadOnlyDictionary<string, ISpecVerificationEvidenceAdapter> _adapters =
        new Dictionary<string, ISpecVerificationEvidenceAdapter>(StringComparer.Ordinal)
        {
            ["build-summary"] = new BuildSummaryEvidenceAdapter(),
            ["secret-scan"] = new SecretScanEvidenceAdapter()
        };

    /// <summary>
    /// Binds bindings to collected evidence. Returns observations on success.
    /// Returns empty list and sets validationErrors on failure.
    /// </summary>
    public IReadOnlyList<SpecVerificationEvidenceObservation> Bind(
        IReadOnlyList<SpecVerificationEvidenceBinding> bindings_,
        IReadOnlyDictionary<string, RepositoryEvidence> collectedEvidence_,
        string repoRoot_,
        out IReadOnlyList<string> validationErrors_)
    {
        ArgumentNullException.ThrowIfNull(bindings_);
        ArgumentNullException.ThrowIfNull(collectedEvidence_);

        List<string> errors = [];
        List<SpecVerificationEvidenceObservation> observations = [];

        // Check for duplicate EVD IDs in request
        HashSet<string> seenEvidenceIds = new(StringComparer.Ordinal);
        foreach (SpecVerificationEvidenceBinding binding in bindings_)
        {
            string evdId = binding.Evidence.Id.Value;
            if (!seenEvidenceIds.Add(evdId))
            {
                errors.Add($"Duplicate verification evidence ID '{evdId}' in request.");
            }
        }

        if (errors.Count > 0)
        {
            validationErrors_ = errors.ToArray();
            return [];
        }

        foreach (SpecVerificationEvidenceBinding binding in bindings_)
        {
            string repoEvidenceId = binding.RepositoryEvidenceId;

            if (!collectedEvidence_.TryGetValue(repoEvidenceId, out RepositoryEvidence? repoEvidence))
            {
                errors.Add($"Repository evidence ID '{repoEvidenceId}' bound to '{binding.Evidence.Id.Value}' is not known to the collector.");
                continue;
            }

            SpecVerificationEvidenceDisposition disposition;
            string reason;

            if (_adapters.TryGetValue(repoEvidenceId, out ISpecVerificationEvidenceAdapter? adapter))
            {
                disposition = adapter.Evaluate(repoEvidence, repoRoot_);
                reason = adapter.GetReason(repoEvidence, disposition);
            }
            else
            {
                // Available but no decisive adapter => Inconclusive
                disposition = SpecVerificationEvidenceDisposition.Inconclusive;
                reason = $"No deterministic adapter for repository evidence source '{repoEvidence.Source}'. Inconclusive by default.";
            }

            observations.Add(new SpecVerificationEvidenceObservation
            {
                EvidenceId = binding.Evidence.Id,
                RepositoryEvidenceId = repoEvidenceId,
                Source = repoEvidence.Source,
                Kind = repoEvidence.Kind,
                Reference = repoEvidence.Reference,
                Availability = repoEvidence.Availability,
                Freshness = repoEvidence.Freshness,
                SourceGeneratedAt = repoEvidence.SourceGeneratedAt,
                Disposition = disposition,
                Reason = reason
            });
        }

        if (errors.Count > 0)
        {
            validationErrors_ = errors.ToArray();
            return [];
        }

        validationErrors_ = [];
        return observations.ToArray();
    }
}
