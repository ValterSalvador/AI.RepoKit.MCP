namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelExecutionResultTests
{
    [Fact]
    public void Constructor_NullResponseText_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionResult(null!));
    }

    [Fact]
    public void Constructor_PreservesExactResponseText()
    {
        string text = "Exact response with \n\r\t and \"special\" chars <xml>";
        ModelExecutionResult result = new(text);

        Assert.Same(text, result.ResponseText);
    }

    [Fact]
    public void Constructor_AllowsEmptyResponse()
    {
        ModelExecutionResult result = new(string.Empty);

        Assert.Equal(string.Empty, result.ResponseText);
    }
}
