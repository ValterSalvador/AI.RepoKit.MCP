namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelExecutionUpdateTests
{
    [Fact]
    public void Constructor_NullResponseTextDelta_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionUpdate(null!));
    }

    [Fact]
    public void Constructor_EmptyString_AcceptedAndRetained()
    {
        ModelExecutionUpdate update = new(string.Empty);
        Assert.Equal(string.Empty, update.ResponseTextDelta);
    }

    [Fact]
    public void Constructor_WhitespaceOnlyString_AcceptedAndRetainedWithoutTrimming()
    {
        string whitespace = "   \t \r\n  ";
        ModelExecutionUpdate update = new(whitespace);
        Assert.Same(whitespace, update.ResponseTextDelta);
    }

    [Fact]
    public void Constructor_ExactTextRetainedWithoutNormalization()
    {
        string text = "Hello \r\n World \t with spaces!";
        ModelExecutionUpdate update = new(text);
        Assert.Same(text, update.ResponseTextDelta);
    }

    [Fact]
    public void RecordEquality_IdenticalDeltas_AreEqual()
    {
        ModelExecutionUpdate update1 = new("delta");
        ModelExecutionUpdate update2 = new("delta");

        Assert.Equal(update1, update2);
        Assert.True(update1 == update2);
    }

    [Fact]
    public void RecordEquality_DifferentDeltas_AreNotEqual()
    {
        ModelExecutionUpdate update1 = new("delta1");
        ModelExecutionUpdate update2 = new("delta2");

        Assert.NotEqual(update1, update2);
        Assert.True(update1 != update2);
    }
}
