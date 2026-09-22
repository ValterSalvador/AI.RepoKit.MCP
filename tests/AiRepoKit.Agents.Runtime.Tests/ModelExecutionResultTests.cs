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

    [Fact]
    public void Constructor_ResponseTextOnly_SetsTelemetryToNull()
    {
        ModelExecutionResult result = new("response");

        Assert.Null(result.Telemetry);
    }

    [Fact]
    public void TelemetryConstructor_RetainsExactResponseTextAndExactTelemetry()
    {
        string text = "exact text";
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.FromMilliseconds(50));
        ModelExecutionResult result = new(text, telemetry);

        Assert.Same(text, result.ResponseText);
        Assert.Same(telemetry, result.Telemetry);
    }

    [Fact]
    public void Constructor_NullTelemetryExplicit_ThrowsArgumentNullException()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionResult("response", null!));

        Assert.Equal("telemetry_", ex.ParamName);
    }

    [Fact]
    public void TelemetryConstructor_NullResponseText_ThrowsArgumentNullException()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.FromMilliseconds(50));
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(
            () => new ModelExecutionResult(null!, telemetry));

        Assert.Equal("responseText_", ex.ParamName);
    }

    [Fact]
    public void TelemetryConstructor_AllowsEmptyResponse()
    {
        ModelExecutionTelemetry telemetry = new(null, TimeSpan.Zero);
        ModelExecutionResult result = new(string.Empty, telemetry);

        Assert.Equal(string.Empty, result.ResponseText);
        Assert.Same(telemetry, result.Telemetry);
    }
}
