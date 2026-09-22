namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelTokenUsageTests
{
    [Fact]
    public void Constructor_AllValuesNull_AcceptedAndRetainedAsNull()
    {
        ModelTokenUsage usage = new();

        Assert.Null(usage.InputTokenCount);
        Assert.Null(usage.OutputTokenCount);
        Assert.Null(usage.TotalTokenCount);
        Assert.Null(usage.CachedInputTokenCount);
        Assert.Null(usage.ReasoningTokenCount);
    }

    [Fact]
    public void Constructor_AllZeros_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(0, 0, 0, 0, 0);

        Assert.Equal(0, usage.InputTokenCount);
        Assert.Equal(0, usage.OutputTokenCount);
        Assert.Equal(0, usage.TotalTokenCount);
        Assert.Equal(0, usage.CachedInputTokenCount);
        Assert.Equal(0, usage.ReasoningTokenCount);
    }

    [Fact]
    public void Constructor_PositiveValues_RetainedExactly()
    {
        ModelTokenUsage usage = new(100, 50, 150, 30, 20);

        Assert.Equal(100, usage.InputTokenCount);
        Assert.Equal(50, usage.OutputTokenCount);
        Assert.Equal(150, usage.TotalTokenCount);
        Assert.Equal(30, usage.CachedInputTokenCount);
        Assert.Equal(20, usage.ReasoningTokenCount);
    }

    [Fact]
    public void Constructor_NegativeInputTokenCount_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(inputTokenCount_: -1));

        Assert.Equal("inputTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeOutputTokenCount_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(outputTokenCount_: -1));

        Assert.Equal("outputTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeTotalTokenCount_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(totalTokenCount_: -1));

        Assert.Equal("totalTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeCachedInputTokenCount_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(cachedInputTokenCount_: -1));

        Assert.Equal("cachedInputTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_NegativeReasoningTokenCount_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(reasoningTokenCount_: -1));

        Assert.Equal("reasoningTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_CachedInputExceedsInput_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(inputTokenCount_: 100, cachedInputTokenCount_: 101));

        Assert.Equal("cachedInputTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_CachedInputEqualsInput_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(inputTokenCount_: 100, cachedInputTokenCount_: 100);

        Assert.Equal(100, usage.InputTokenCount);
        Assert.Equal(100, usage.CachedInputTokenCount);
    }

    [Fact]
    public void Constructor_CachedInputLessThanInput_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(inputTokenCount_: 100, cachedInputTokenCount_: 80);

        Assert.Equal(100, usage.InputTokenCount);
        Assert.Equal(80, usage.CachedInputTokenCount);
    }

    [Fact]
    public void Constructor_CachedInputSuppliedWithoutInput_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(cachedInputTokenCount_: 40);

        Assert.Null(usage.InputTokenCount);
        Assert.Equal(40, usage.CachedInputTokenCount);
    }

    [Fact]
    public void Constructor_ReasoningExceedsOutput_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelTokenUsage(outputTokenCount_: 50, reasoningTokenCount_: 51));

        Assert.Equal("reasoningTokenCount_", ex.ParamName);
    }

    [Fact]
    public void Constructor_ReasoningEqualsOutput_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(outputTokenCount_: 50, reasoningTokenCount_: 50);

        Assert.Equal(50, usage.OutputTokenCount);
        Assert.Equal(50, usage.ReasoningTokenCount);
    }

    [Fact]
    public void Constructor_ReasoningLessThanOutput_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(outputTokenCount_: 50, reasoningTokenCount_: 25);

        Assert.Equal(50, usage.OutputTokenCount);
        Assert.Equal(25, usage.ReasoningTokenCount);
    }

    [Fact]
    public void Constructor_ReasoningSuppliedWithoutOutput_AcceptedAndRetained()
    {
        ModelTokenUsage usage = new(reasoningTokenCount_: 30);

        Assert.Null(usage.OutputTokenCount);
        Assert.Equal(30, usage.ReasoningTokenCount);
    }

    [Fact]
    public void Constructor_TotalRemainsNull_WhenNotSupplied()
    {
        ModelTokenUsage usage = new(inputTokenCount_: 100, outputTokenCount_: 50);

        Assert.Null(usage.TotalTokenCount);
    }

    [Fact]
    public void Constructor_TotalRetainedExactly_WhenSupplied()
    {
        ModelTokenUsage usage = new(totalTokenCount_: 999);

        Assert.Equal(999, usage.TotalTokenCount);
    }

    [Fact]
    public void Constructor_NoTotalDerivation_WhenInputAndOutputProvidedWithoutTotal()
    {
        ModelTokenUsage usage = new(inputTokenCount_: 123, outputTokenCount_: 456);

        Assert.Null(usage.TotalTokenCount);
    }

    [Fact]
    public void Constructor_TotalDoesNotEqualInputPlusOutput_IsAllowedAndRetained()
    {
        ModelTokenUsage usage = new(inputTokenCount_: 10, outputTokenCount_: 20, totalTokenCount_: 999);

        Assert.Equal(10, usage.InputTokenCount);
        Assert.Equal(20, usage.OutputTokenCount);
        Assert.Equal(999, usage.TotalTokenCount);
    }

    [Fact]
    public void PublicProperties_AreReadOnly()
    {
        PropertyInfo[] properties = typeof(ModelTokenUsage).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);
        foreach (PropertyInfo property in properties)
        {
            Assert.Null(property.SetMethod);
        }
    }

    [Fact]
    public void RecordEquality_IdenticalValues_AreEqual()
    {
        ModelTokenUsage usage1 = new(100, 50, 150, 20, 10);
        ModelTokenUsage usage2 = new(100, 50, 150, 20, 10);

        Assert.Equal(usage1, usage2);
        Assert.True(usage1 == usage2);
    }

    [Fact]
    public void RecordEquality_DifferentValues_AreNotEqual()
    {
        ModelTokenUsage usage1 = new(100, 50, 150, 20, 10);
        ModelTokenUsage usage2 = new(100, 51, 150, 20, 10);

        Assert.NotEqual(usage1, usage2);
        Assert.True(usage1 != usage2);
    }
}
