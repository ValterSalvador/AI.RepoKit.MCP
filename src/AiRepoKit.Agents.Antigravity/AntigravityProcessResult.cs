namespace AiRepoKit.Agents.Antigravity;

internal sealed record AntigravityProcessResult
{
    public int ExitCode
    {
        get;
    }

    public string StandardOutput
    {
        get;
    }

    public string StandardError
    {
        get;
    }

    public AntigravityProcessResult(
        int exitCode_,
        string standardOutput_,
        string standardError_)
    {
        ArgumentNullException.ThrowIfNull(
            standardOutput_,
            nameof(standardOutput_));

        ArgumentNullException.ThrowIfNull(
            standardError_,
            nameof(standardError_));

        this.ExitCode =
            exitCode_;
        this.StandardOutput =
            standardOutput_;
        this.StandardError =
            standardError_;
    }
}
