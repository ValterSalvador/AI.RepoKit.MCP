namespace AiRepoKit.Agents.Runtime;

public sealed record ModelTokenPricing
{
    public string CurrencyCode
    {
        get;
    }

    public decimal InputCostPerMillionTokens
    {
        get;
    }

    public decimal OutputCostPerMillionTokens
    {
        get;
    }

    public decimal? CachedInputCostPerMillionTokens
    {
        get;
    }

    public ModelTokenPricing(
        string currencyCode_,
        decimal inputCostPerMillionTokens_,
        decimal outputCostPerMillionTokens_,
        decimal? cachedInputCostPerMillionTokens_ = null)
    {
        CurrencyCodeValidator.Validate(currencyCode_, nameof(currencyCode_));

        if (inputCostPerMillionTokens_ < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputCostPerMillionTokens_),
                inputCostPerMillionTokens_,
                "Input cost per million tokens must be non-negative.");
        }

        if (outputCostPerMillionTokens_ < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputCostPerMillionTokens_),
                outputCostPerMillionTokens_,
                "Output cost per million tokens must be non-negative.");
        }

        if (cachedInputCostPerMillionTokens_.HasValue && cachedInputCostPerMillionTokens_.Value < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cachedInputCostPerMillionTokens_),
                cachedInputCostPerMillionTokens_.Value,
                "Cached input cost per million tokens must be non-negative when specified.");
        }

        this.CurrencyCode = currencyCode_;
        this.InputCostPerMillionTokens = inputCostPerMillionTokens_;
        this.OutputCostPerMillionTokens = outputCostPerMillionTokens_;
        this.CachedInputCostPerMillionTokens = cachedInputCostPerMillionTokens_;
    }
}

internal static class CurrencyCodeValidator
{
    public static void Validate(string currencyCode_, string paramName_)
    {
        ArgumentNullException.ThrowIfNull(currencyCode_, paramName_);

        if (currencyCode_.Length != 3)
        {
            throw new ArgumentException(
                "Currency code must be exactly 3 uppercase ASCII letters.",
                paramName_);
        }

        for (int i = 0; i < 3; i++)
        {
            char c = currencyCode_[i];
            if (c < 'A' || c > 'Z')
            {
                throw new ArgumentException(
                    "Currency code must be exactly 3 uppercase ASCII letters.",
                    paramName_);
            }
        }
    }
}
