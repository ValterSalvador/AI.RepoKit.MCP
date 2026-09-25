namespace AiRepoKit.Execution;

public static class ContextCompiler
{
    public const string AlgorithmId =
        "ai.repokit.context-compiler/v1";

    public static CompiledContext Compile(
        IReadOnlyList<ContextCandidate> candidates_,
        int tokenBudget_,
        int itemLimit_)
    {
        ArgumentNullException.ThrowIfNull(
            candidates_,
            nameof(candidates_));

        if (tokenBudget_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tokenBudget_),
                tokenBudget_,
                "Token budget must be greater than zero.");
        }

        if (itemLimit_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(itemLimit_),
                itemLimit_,
                "Item limit must be greater than zero.");
        }

        HashSet<string> candidateIds =
            new(StringComparer.Ordinal);

        ContextCandidate[] validatedCandidates =
            new ContextCandidate[candidates_.Count];

        for (
            int index = 0;
            index < candidates_.Count;
            index++
        )
        {
            ContextCandidate? candidate =
                candidates_[index];

            if (candidate is null)
            {
                throw new ArgumentException(
                    "Candidates must not contain null.",
                    nameof(candidates_));
            }

            if (!candidateIds.Add(candidate.Id))
            {
                throw new ArgumentException(
                    $"Duplicate candidate id '{candidate.Id}'.",
                    nameof(candidates_));
            }

            validatedCandidates[index] =
                candidate;
        }

        ContextCandidate[] rankedCandidates =
            validatedCandidates
                .OrderByDescending(
                    candidate_ =>
                        candidate_.RelevanceScore)
                .ThenBy(
                    candidate_ =>
                        candidate_.Id,
                    StringComparer.Ordinal)
                .ToArray();

        List<CompiledContextItem> selectedItems =
            new();

        List<string> omittedCandidateIds =
            new();

        int estimatedTokens =
            0;

        foreach (
            ContextCandidate candidate
            in rankedCandidates)
        {
            int candidateEstimatedTokens =
                DeterministicWorkEstimator.EstimateTokens(
                    candidate.Content);

            if (selectedItems.Count >= itemLimit_)
            {
                omittedCandidateIds.Add(
                    candidate.Id);

                continue;
            }

            if (
                (long) estimatedTokens +
                candidateEstimatedTokens >
                tokenBudget_)
            {
                omittedCandidateIds.Add(
                    candidate.Id);

                continue;
            }

            selectedItems.Add(
                new CompiledContextItem(
                    candidate.Id,
                    candidate.Content,
                    candidate.RelevanceScore,
                    candidateEstimatedTokens));

            estimatedTokens =
                checked(
                    estimatedTokens +
                    candidateEstimatedTokens);
        }

        return new CompiledContext(
            tokenBudget_,
            itemLimit_,
            estimatedTokens,
            omittedCandidateIds.Count > 0,
            selectedItems,
            omittedCandidateIds);
    }
}
