namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelExecutionRequestTests
{
    [Fact]
    public void Constructor_NullPrompt_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionRequest(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n")]
    public void Constructor_BlankPrompt_ThrowsArgumentException(string blankPrompt)
    {
        Assert.Throws<ArgumentException>(
            () => new ModelExecutionRequest(blankPrompt));
    }

    [Fact]
    public void Constructor_PreservesExactPrompt_IncludingWhitespaceAndNewlines()
    {
        string prompt = "  line1 \r\n line2 \t \n line3  ";
        ModelExecutionRequest request = new(prompt);

        Assert.Same(prompt, request.Prompt);
    }

    [Fact]
    public void Constructor_NullStructuredOutput_DefaultsToNull()
    {
        ModelExecutionRequest request = new("My prompt");

        Assert.Null(request.StructuredOutput);
    }

    [Fact]
    public void Constructor_RetainsExactStructuredOutputInstance()
    {
        StructuredOutputContract contract = new("{\"type\":\"object\"}");
        ModelExecutionRequest request = new("My prompt", contract);

        Assert.Same(contract, request.StructuredOutput);
    }

    [Fact]
    public void Constructor_WithoutTimeout_DefaultsToNull()
    {
        ModelExecutionRequest request = new("My prompt");
        Assert.Null(request.Timeout);
    }

    [Fact]
    public void Constructor_ExplicitNullTimeout_Accepted()
    {
        ModelExecutionRequest request = new("My prompt", structuredOutput_: null, timeout_: null);
        Assert.Null(request.Timeout);
    }

    [Fact]
    public void Constructor_PositiveTimeout_RetainsExactValue()
    {
        TimeSpan timeout = TimeSpan.FromSeconds(30);
        ModelExecutionRequest request = new("My prompt", structuredOutput_: null, timeout_: timeout);
        Assert.Equal(timeout, request.Timeout);
    }

    [Fact]
    public void Constructor_ZeroTimeout_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelExecutionRequest("My prompt", timeout_: TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-500)]
    public void Constructor_NegativeTimeout_ThrowsArgumentOutOfRangeException(int negativeMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelExecutionRequest("My prompt", timeout_: TimeSpan.FromMilliseconds(negativeMs)));
    }

    [Fact]
    public void Constructor_UpperBoundaryTimeout_Accepted()
    {
        TimeSpan boundaryTimeout = TimeSpan.FromMilliseconds(4294967294L);
        ModelExecutionRequest request = new("My prompt", timeout_: boundaryTimeout);
        Assert.Equal(boundaryTimeout, request.Timeout);
    }

    [Fact]
    public void Constructor_AboveUpperBoundaryTimeout_ThrowsArgumentOutOfRangeException()
    {
        TimeSpan aboveBoundary = TimeSpan.FromMilliseconds(4294967295L);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelExecutionRequest("My prompt", timeout_: aboveBoundary));
    }

    [Fact]
    public void Constructor_TimeSpanMaxValue_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelExecutionRequest("My prompt", timeout_: TimeSpan.MaxValue));
    }
}
