using System.Globalization;
using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Cli.Services.ContextBudget;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Services.SpecContexts;

public sealed class SpecContextBuilder :
    ISpecContextBuilder
{
    private const string ReferenceLimitReason =
        "Reference limit exceeded; lower-priority item omitted.";
    private const string BudgetReason =
        "Budget exceeded; lower-priority item omitted.";

    private readonly IRepositoryEvidenceCollector _repositoryEvidenceCollector;
    private readonly ContextBudgeter _contextBudgeter;

    public SpecContextBuilder()
        : this(
            new RepositoryEvidenceCollector(),
            new ContextBudgeter())
    {
    }

    internal SpecContextBuilder(
        IRepositoryEvidenceCollector repositoryEvidenceCollector_,
        ContextBudgeter contextBudgeter_)
    {
        this._repositoryEvidenceCollector =
            repositoryEvidenceCollector_ ??
            throw new ArgumentNullException(nameof(repositoryEvidenceCollector_));
        this._contextBudgeter =
            contextBudgeter_ ??
            throw new ArgumentNullException(nameof(contextBudgeter_));
    }

    public SpecContext Build(
        SpecContextBuildRequest request_)
    {
        ArgumentNullException.ThrowIfNull(request_);

        if (string.IsNullOrWhiteSpace(request_.RepoRoot))
        {
            throw new ArgumentException(
                "Repository root must not be blank.",
                nameof(request_));
        }

        if (!SpecId.IsValid(request_.SpecId))
        {
            throw new ArgumentException(
                "Spec ID is invalid.",
                nameof(request_));
        }

        ArgumentNullException.ThrowIfNull(request_.RequirementSet);
        ArgumentNullException.ThrowIfNull(request_.WorkSpec);

        if (request_.ReferenceLimit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request_),
                "Reference limit must be between 1 and 100 inclusive.");
        }

        if (request_.Budget <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request_),
                "Budget must be greater than zero.");
        }

        if (request_.MaxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request_),
                "Maximum files must be greater than zero.");
        }

        RepositoryEvidenceCollection collection =
            this._repositoryEvidenceCollector.Collect(
                new RepositoryEvidenceCollectionRequest(
                    request_.RepoRoot,
                    request_.Target,
                    request_.ReferenceLimit,
                    request_.MaxFiles));

        IReadOnlyList<SpecContextReference> ordered =
            collection.ReferenceCandidates
                .OrderByDescending(reference_ => reference_.Priority)
                .ThenBy(StableIdentity, StringComparer.Ordinal)
                .ThenBy(reference_ => reference_.EvidenceId, StringComparer.Ordinal)
                .ToArray();
        List<SpecContextReference> deduplicated = [];
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (SpecContextReference candidate in ordered)
        {
            if (identities.Add(StableIdentity(candidate)))
            {
                deduplicated.Add(candidate);
            }
        }

        IReadOnlyList<SpecContextReference> admitted =
            deduplicated
                .Take(request_.ReferenceLimit)
                .ToArray();
        Dictionary<string, string> ranks =
            admitted
                .Select((candidate_, index_) => new
                {
                    Identity = StableIdentity(candidate_),
                    Rank = index_.ToString("D8", CultureInfo.InvariantCulture)
                })
                .ToDictionary(
                    item_ => item_.Identity,
                    item_ => item_.Rank,
                    StringComparer.Ordinal);
        var budgetResult =
            this._contextBudgeter.Apply(
                admitted,
                request_.Budget,
                candidate_ => ranks[StableIdentity(candidate_)],
                candidate_ => candidate_.Priority);
        HashSet<string> selectedIdentities =
            new(
                budgetResult.Value.Select(StableIdentity),
                StringComparer.Ordinal);
        IReadOnlyList<SpecContextReference> references =
            budgetResult.Value
                .OrderByDescending(reference_ => reference_.Priority)
                .ThenBy(StableIdentity, StringComparer.Ordinal)
                .ThenBy(reference_ => reference_.EvidenceId, StringComparer.Ordinal)
                .ToArray();

        List<SpecContextOmission> omissions = [];
        for (int index = 0; index < deduplicated.Count; index++)
        {
            SpecContextReference candidate = deduplicated[index];
            string reason;
            if (index >= request_.ReferenceLimit)
            {
                reason = ReferenceLimitReason;
            }
            else if (!selectedIdentities.Contains(StableIdentity(candidate)))
            {
                reason = BudgetReason;
            }
            else
            {
                continue;
            }

            omissions.Add(
                new SpecContextOmission
                {
                    Reference = candidate.Reference,
                    Reason = reason,
                    RemovedEstimatedTokens = ContextBudgeter.EstimateTokens(candidate)
                });
        }

        SpecContext specContext =
            new()
            {
                SpecId = request_.SpecId,
                RequirementSetRevision = request_.RequirementSet.Revision,
                WorkSpecRevision = request_.WorkSpec.Revision,
                Target = request_.Target,
                ReferenceLimit = request_.ReferenceLimit,
                Budget = request_.Budget,
                EstimatedTokens = budgetResult.EstimatedTokens,
                Truncated = omissions.Count > 0,
                Evidence = collection.Evidence
                    .OrderBy(evidence_ => evidence_.EvidenceId, StringComparer.Ordinal)
                    .ToArray(),
                References = references,
                Omissions = omissions
            };
        IReadOnlyList<AiRepoKit.Spec.SpecValidationError> errors =
            SpecContextValidator.Validate(specContext);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "SpecContext validation failed: " +
                string.Join(
                    " | ",
                    errors.Select(error_ => $"{error_.Code}: {error_.Message}")));
        }

        return specContext;
    }

    private static string StableIdentity(
        SpecContextReference reference_)
    {
        return reference_.Kind + "\u001f" + reference_.Reference;
    }
}
