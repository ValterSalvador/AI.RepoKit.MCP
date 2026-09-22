namespace AiRepoKit.Agents.Runtime;

public sealed record ModelExecutionTelemetry
{
    public ModelTokenUsage? TokenUsage
    {
        get;
    }

    public TimeSpan Latency
    {
        get;
    }

    public decimal? EstimatedCost
    {
        get;
    }

    public string? CostCurrencyCode
    {
        get;
    }

    public ModelExecutionTelemetry(
        ModelTokenUsage? tokenUsage_,
        TimeSpan latency_,
        decimal? estimatedCost_ = null,
        string? costCurrencyCode_ = null)
    {
        if (latency_ < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(latency_),
                latency_,
                "Latency must be non-negative.");
        }

        if (estimatedCost_.HasValue)
        {
            if (estimatedCost_.Value < 0m)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(estimatedCost_),
                    estimatedCost_.Value,
                    "Estimated cost must be non-negative.");
            }

            if (costCurrencyCode_ is null)
            {
                throw new ArgumentException(
                    "Cost currency code must be provided when estimated cost is specified.",
                    nameof(costCurrencyCode_));
            }

            CurrencyCodeValidator.Validate(costCurrencyCode_, nameof(costCurrencyCode_));
        }
        else
        {
            if (costCurrencyCode_ is not null)
            {
                throw new ArgumentException(
                    "Cost currency code cannot be provided when estimated cost is null.",
                    nameof(costCurrencyCode_));
            }
        }

        this.TokenUsage = tokenUsage_;
        this.Latency = latency_;
        this.EstimatedCost = estimatedCost_;
        this.CostCurrencyCode = costCurrencyCode_;
    }
}
