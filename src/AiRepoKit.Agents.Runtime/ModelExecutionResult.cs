namespace AiRepoKit.Agents.Runtime;

public sealed record ModelExecutionResult
{
    public string ResponseText
    {
        get;
    }

    public ModelExecutionTelemetry? Telemetry
    {
        get;
    }

    public ModelExecutionResult(
        string responseText_)
    {
        ArgumentNullException.ThrowIfNull(
            responseText_,
            nameof(responseText_));

        this.ResponseText =
            responseText_;
        this.Telemetry = null;
    }

    public ModelExecutionResult(
        string responseText_,
        ModelExecutionTelemetry telemetry_)
    {
        ArgumentNullException.ThrowIfNull(
            responseText_,
            nameof(responseText_));
        ArgumentNullException.ThrowIfNull(
            telemetry_,
            nameof(telemetry_));

        this.ResponseText =
            responseText_;
        this.Telemetry =
            telemetry_;
    }
}
