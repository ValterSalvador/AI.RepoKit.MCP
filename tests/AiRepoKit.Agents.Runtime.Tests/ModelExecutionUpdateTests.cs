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

    [Fact]
    public void Constructor_DeltaOnly_SetsTelemetryToNull()
    {
        ModelExecutionUpdate update = new("delta");

        Assert.Null(update.Telemetry);
    }

    [Fact]
    public void TelemetryConstructor_RetainsExactDeltaAndExactTelemetry()
    {
        string delta = "delta";
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.FromMilliseconds(10));
        ModelExecutionUpdate update = new(delta, telemetry);

        Assert.Same(delta, update.ResponseTextDelta);
        Assert.Same(telemetry, update.Telemetry);
    }

    [Fact]
    public void Constructor_NullTelemetryExplicit_ThrowsArgumentNullException()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionUpdate("delta", null!));

        Assert.Equal("telemetry_", ex.ParamName);
    }

    [Fact]
    public void TelemetryConstructor_AcceptsEmptyDeltaWithTelemetry()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero);
        ModelExecutionUpdate update = new(string.Empty, telemetry);

        Assert.Equal(string.Empty, update.ResponseTextDelta);
        Assert.Same(telemetry, update.Telemetry);
    }

    [Fact]
    public void TelemetryConstructor_NullDelta_ThrowsArgumentNullException()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero);
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionUpdate(null!, telemetry));

        Assert.Equal("responseTextDelta_", ex.ParamName);
    }

    [Fact]
    public void RecordEquality_SameDeltaAndTelemetry_AreEqual()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.FromMilliseconds(5));
        ModelExecutionUpdate update1 = new("delta", telemetry);
        ModelExecutionUpdate update2 = new("delta", telemetry);

        Assert.Equal(update1, update2);
        Assert.True(update1 == update2);
    }

    [Fact]
    public void RecordEquality_SameDeltaDifferentTelemetry_AreNotEqual()
    {
        ModelExecutionTelemetry telemetry1 = new(null, TimeSpan.FromMilliseconds(5));
        ModelExecutionTelemetry telemetry2 = new(null, TimeSpan.FromMilliseconds(10));
        ModelExecutionUpdate update1 = new("delta", telemetry1);
        ModelExecutionUpdate update2 = new("delta", telemetry2);

        Assert.NotEqual(update1, update2);
        Assert.True(update1 != update2);
    }
}
