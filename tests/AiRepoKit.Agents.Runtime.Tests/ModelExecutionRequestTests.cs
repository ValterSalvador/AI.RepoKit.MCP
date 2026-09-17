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
}
