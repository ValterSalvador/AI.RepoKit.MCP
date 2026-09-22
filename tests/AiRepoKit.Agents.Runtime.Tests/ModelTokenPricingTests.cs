namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelTokenPricingTests
{
    [Fact]
    public void Constructor_ValidUsdCurrencyCode_AcceptedAndRetained()
    {
        ModelTokenPricing pricing = new("USD", 1.50m, 2.50m, 0.75m);

        Assert.Equal("USD", pricing.CurrencyCode);
        Assert.Equal(1.50m, pricing.InputCostPerMillionTokens);
        Assert.Equal(2.50m, pricing.OutputCostPerMillionTokens);
        Assert.Equal(0.75m, pricing.CachedInputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_AnotherValidCurrencyCode_AcceptedAndRetained()
    {
        ModelTokenPricing pricing = new("EUR", 2.00m, 4.00m);

        Assert.Equal("EUR", pricing.CurrencyCode);
        Assert.Equal(2.00m, pricing.InputCostPerMillionTokens);
        Assert.Equal(4.00m, pricing.OutputCostPerMillionTokens);
        Assert.Null(pricing.CachedInputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_NullCurrencyCode_ThrowsArgumentNullException()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => new ModelTokenPricing(null!, 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_EmptyCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing(string.Empty, 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_WhitespaceCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("   ", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_TwoCharacterCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("US", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_FourCharacterCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("USDD", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_LowercaseCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("usd", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_MixedCaseCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("Usd", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NonAsciiCurrencyCode_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("€UR", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_CurrencyWithWhitespace_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelTokenPricing("US ", 1.0m, 2.0m));

        Assert.Equal("currencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_ExactCurrencyRetainedWithoutTrimmingOrCaseConversion()
    {
        string currency = "BRL";
        ModelTokenPricing pricing = new(currency, 1.0m, 2.0m);

        Assert.Same(currency, pricing.CurrencyCode);
    }

    [Fact]
    public void Constructor_PositiveRates_RetainedExactly()
    {
        ModelTokenPricing pricing = new("USD", 1.23456789m, 9.87654321m, 0.55555555m);

        Assert.Equal(1.23456789m, pricing.InputCostPerMillionTokens);
        Assert.Equal(9.87654321m, pricing.OutputCostPerMillionTokens);
        Assert.Equal(0.55555555m, pricing.CachedInputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_ZeroInputRate_AcceptedAndRetained()
    {
        ModelTokenPricing pricing = new("USD", 0m, 2.0m);

        Assert.Equal(0m, pricing.InputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_ZeroOutputRate_AcceptedAndRetained()
    {
        ModelTokenPricing pricing = new("USD", 1.0m, 0m);

        Assert.Equal(0m, pricing.OutputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_ZeroCachedRate_AcceptedAndRetained()
    {
        ModelTokenPricing pricing = new("USD", 1.0m, 2.0m, 0m);

        Assert.Equal(0m, pricing.CachedInputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_NullCachedRate_AcceptedAndRetainedAsNull()
    {
        ModelTokenPricing pricing = new("USD", 1.0m, 2.0m, null);

        Assert.Null(pricing.CachedInputCostPerMillionTokens);
    }

    [Fact]
    public void Constructor_NegativeInputRate_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenPricing("USD", -0.01m, 2.0m));

        Assert.Equal("inputCostPerMillionTokens_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeOutputRate_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenPricing("USD", 1.0m, -0.01m));

        Assert.Equal("outputCostPerMillionTokens_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeCachedRate_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenPricing("USD", 1.0m, 2.0m, -0.01m));

        Assert.Equal("cachedInputCostPerMillionTokens_", ex.ParamName);
    }

    [Fact]
    public void PublicProperties_AreReadOnly()
    {
        PropertyInfo[] properties = typeof(ModelTokenPricing).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);
        foreach (PropertyInfo property in properties)
        {
            Assert.Null(property.SetMethod);
        }
    }

    [Fact]
    public void RecordEquality_IdenticalValues_AreEqual()
    {
        ModelTokenPricing pricing1 = new("USD", 1.0m, 2.0m, 0.5m);
        ModelTokenPricing pricing2 = new("USD", 1.0m, 2.0m, 0.5m);

        Assert.Equal(pricing1, pricing2);
        Assert.True(pricing1 == pricing2);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        ModelTokenPricing pricing1 = new("USD", 1.0m, 2.0m, 0.5m);
        ModelTokenPricing pricing2 = new("EUR", 1.0m, 2.0m, 0.5m);

        Assert.NotEqual(pricing1, pricing2);
        Assert.True(pricing1 != pricing2);
    }
}
