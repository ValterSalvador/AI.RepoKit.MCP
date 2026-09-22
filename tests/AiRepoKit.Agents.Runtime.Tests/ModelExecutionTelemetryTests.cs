namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelExecutionTelemetryTests
{
    [Fact]
    public void Constructor_NullTokenUsage_AcceptedAndRetainedAsNull()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.FromMilliseconds(100));

        Assert.Null(telemetry.TokenUsage);
        Assert.Equal(TimeSpan.FromMilliseconds(100), telemetry.Latency);
        Assert.Null(telemetry.EstimatedCost);
        Assert.Null(telemetry.CostCurrencyCode);
    }

    [Fact]
    public void Constructor_ExactTokenUsageReference_Retained()
    {
        ModelTokenUsage usage = new(10, 20, 30);
        ModelExecutionTelemetry telemetry = new(usage, TimeSpan.FromMilliseconds(50));

        Assert.Same(usage, telemetry.TokenUsage);
    }

    [Fact]
    public void Constructor_ZeroLatency_AcceptedAndRetained()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, telemetry.Latency);
    }

    [Fact]
    public void Constructor_PositiveLatency_RetainedExactly()
    {
        TimeSpan latency = TimeSpan.FromTicks(1234567);
        ModelExecutionTelemetry telemetry = new(null, latency);

        Assert.Equal(latency, telemetry.Latency);
    }

    [Fact]
    public void Constructor_NegativeLatency_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.FromMilliseconds(-1)));

        Assert.Equal("latency_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullCostAndNullCurrency_AcceptedAndRetainedAsNull()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero, null, null);

        Assert.Null(telemetry.EstimatedCost);
        Assert.Null(telemetry.CostCurrencyCode);
    }

    [Fact]
    public void Constructor_PositiveCostPlusValidCurrency_AcceptedAndRetained()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero, 0.0042m, "USD");

        Assert.Equal(0.0042m, telemetry.EstimatedCost);
        Assert.Equal("USD", telemetry.CostCurrencyCode);
    }

    [Fact]
    public void Constructor_ZeroCost_AcceptedAndRetained()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero, 0m, "USD");

        Assert.Equal(0m, telemetry.EstimatedCost);
        Assert.Equal("USD", telemetry.CostCurrencyCode);
    }

    [Fact]
    public void Constructor_NegativeCost_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.Zero, -0.001m, "USD"));

        Assert.Equal("estimatedCost_", ex.ParamName);
    }

    [Fact]
    public void Constructor_CostWithoutCurrency_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.Zero, 1.0m, null));

        Assert.Equal("costCurrencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_CurrencyWithoutCost_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.Zero, null, "USD"));

        Assert.Equal("costCurrencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_InvalidCurrencyLength_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.Zero, 1.0m, "US"));

        Assert.Equal("costCurrencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_LowercaseCurrency_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.Zero, 1.0m, "usd"));

        Assert.Equal("costCurrencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NonAsciiCurrency_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => new ModelExecutionTelemetry(null, TimeSpan.Zero, 1.0m, "US$"));

        Assert.Equal("costCurrencyCode_", ex.ParamName);
    }

    [Fact]
    public void Constructor_ExactEstimatedCostRetainedWithoutRounding()
    {
        decimal cost = 0.1234567890123456789m;
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero, cost, "USD");

        Assert.Equal(cost, telemetry.EstimatedCost);
    }

    [Fact]
    public void Constructor_ExactCurrencyRetainedWithoutTrimmingOrCaseConversion()
    {
        string currency = "EUR";
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero, 1.0m, currency);

        Assert.Same(currency, telemetry.CostCurrencyCode);
    }

    [Fact]
    public void PublicProperties_AreReadOnly()
    {
        PropertyInfo[] properties = typeof(ModelExecutionTelemetry).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);
        foreach (PropertyInfo property in properties)
        {
            Assert.Null(property.SetMethod);
        }
    }

    [Fact]
    public void RecordEquality_IdenticalValues_AreEqual()
    {
        ModelTokenUsage usage = new(1, 2, 3);
        ModelExecutionTelemetry t1 = new(usage, TimeSpan.FromSeconds(1), 0.5m, "USD");
        ModelExecutionTelemetry t2 = new(usage, TimeSpan.FromSeconds(1), 0.5m, "USD");

        Assert.Equal(t1, t2);
        Assert.True(t1 == t2);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        ModelExecutionTelemetry t1 = new(null, TimeSpan.FromSeconds(1), 0.5m, "USD");
        ModelExecutionTelemetry t2 = new(null, TimeSpan.FromSeconds(2), 0.5m, "USD");

        Assert.NotEqual(t1, t2);
        Assert.True(t1 != t2);
    }
}
