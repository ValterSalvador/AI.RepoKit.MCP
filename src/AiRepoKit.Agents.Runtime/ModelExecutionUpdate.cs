namespace AiRepoKit.Agents.Runtime;

public sealed record ModelExecutionUpdate
{
    public string ResponseTextDelta
    {
        get;
    }

    public ModelExecutionTelemetry? Telemetry
    {
        get;
    }

    public ModelExecutionUpdate(string responseTextDelta_)
    {
        ArgumentNullException.ThrowIfNull(
            responseTextDelta_,
            nameof(responseTextDelta_));

        this.ResponseTextDelta =
            responseTextDelta_;
        this.Telemetry = null;
    }

    public ModelExecutionUpdate(
        string responseTextDelta_,
        ModelExecutionTelemetry telemetry_)
    {
        ArgumentNullException.ThrowIfNull(
            responseTextDelta_,
            nameof(responseTextDelta_));
        ArgumentNullException.ThrowIfNull(
            telemetry_,
            nameof(telemetry_));

        this.ResponseTextDelta =
            responseTextDelta_;
        this.Telemetry =
            telemetry_;
    }
}
