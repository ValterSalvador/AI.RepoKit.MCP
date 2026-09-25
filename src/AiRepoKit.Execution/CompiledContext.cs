namespace AiRepoKit.Execution;

public sealed record CompiledContext
{
    public int TokenBudget
    {
        get;
    }

    public int ItemLimit
    {
        get;
    }

    public int EstimatedTokens
    {
        get;
    }

    public bool Truncated
    {
        get;
    }

    public IReadOnlyList<CompiledContextItem> Items
    {
        get;
    }

    public IReadOnlyList<string> OmittedCandidateIds
    {
        get;
    }

    public CompiledContext(
        int tokenBudget_,
        int itemLimit_,
        int estimatedTokens_,
        bool truncated_,
        IReadOnlyList<CompiledContextItem> items_,
        IReadOnlyList<string> omittedCandidateIds_)
    {
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

        if (estimatedTokens_ < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedTokens_),
                estimatedTokens_,
                "Estimated tokens must not be negative.");
        }

        if (estimatedTokens_ > tokenBudget_)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedTokens_),
                estimatedTokens_,
                "Estimated tokens must not exceed token budget.");
        }

        ArgumentNullException.ThrowIfNull(
            items_,
            nameof(items_));

        ArgumentNullException.ThrowIfNull(
            omittedCandidateIds_,
            nameof(omittedCandidateIds_));

        CompiledContextItem[] itemSnapshot =
            new CompiledContextItem[items_.Count];

        HashSet<string> selectedIds =
            new(StringComparer.Ordinal);

        int calculatedEstimatedTokens =
            0;

        for (int index = 0; index < items_.Count; index++)
        {
            CompiledContextItem? item =
                items_[index];

            if (item is null)
            {
                throw new ArgumentException(
                    "Items must not contain null.",
                    nameof(items_));
            }

            if (!selectedIds.Add(item.Id))
            {
                throw new ArgumentException(
                    $"Duplicate selected context item id '{item.Id}'.",
                    nameof(items_));
            }

            calculatedEstimatedTokens =
                checked(
                    calculatedEstimatedTokens +
                    item.EstimatedTokens);

            itemSnapshot[index] =
                item;
        }

        if (itemSnapshot.Length > itemLimit_)
        {
            throw new ArgumentException(
                "Item count must not exceed item limit.",
                nameof(items_));
        }

        if (calculatedEstimatedTokens != estimatedTokens_)
        {
            throw new ArgumentException(
                "Estimated token total must equal the sum of item estimates.",
                nameof(estimatedTokens_));
        }

        string[] omittedSnapshot =
            new string[omittedCandidateIds_.Count];

        HashSet<string> omittedIds =
            new(StringComparer.Ordinal);

        for (
            int index = 0;
            index < omittedCandidateIds_.Count;
            index++
        )
        {
            string? omittedId =
                omittedCandidateIds_[index];

            if (string.IsNullOrWhiteSpace(omittedId))
            {
                throw new ArgumentException(
                    "Omitted candidate ids must not be blank.",
                    nameof(omittedCandidateIds_));
            }

            if (!omittedIds.Add(omittedId))
            {
                throw new ArgumentException(
                    $"Duplicate omitted candidate id '{omittedId}'.",
                    nameof(omittedCandidateIds_));
            }

            if (selectedIds.Contains(omittedId))
            {
                throw new ArgumentException(
                    $"Candidate id '{omittedId}' cannot be both selected and omitted.",
                    nameof(omittedCandidateIds_));
            }

            omittedSnapshot[index] =
                omittedId;
        }

        bool expectedTruncated =
            omittedSnapshot.Length > 0;

        if (truncated_ != expectedTruncated)
        {
            throw new ArgumentException(
                "Truncated must match whether candidates were omitted.",
                nameof(truncated_));
        }

        this.TokenBudget =
            tokenBudget_;
        this.ItemLimit =
            itemLimit_;
        this.EstimatedTokens =
            estimatedTokens_;
        this.Truncated =
            truncated_;
        this.Items =
            Array.AsReadOnly(
                itemSnapshot);
        this.OmittedCandidateIds =
            Array.AsReadOnly(
                omittedSnapshot);
    }
}
