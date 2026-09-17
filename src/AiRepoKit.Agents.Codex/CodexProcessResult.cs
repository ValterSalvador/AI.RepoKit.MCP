namespace AiRepoKit.Agents.Codex;

internal sealed record CodexProcessResult
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

    public CodexProcessResult(
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
