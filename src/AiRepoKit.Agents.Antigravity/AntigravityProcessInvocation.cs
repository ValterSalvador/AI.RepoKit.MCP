namespace AiRepoKit.Agents.Antigravity;

internal sealed record AntigravityProcessInvocation
{
    public string Executable
    {
        get;
    }

    public IReadOnlyList<string> Arguments
    {
        get;
    }

    public string WorkingDirectory
    {
        get;
    }

    public AntigravityProcessInvocation(
        string executable_,
        IReadOnlyList<string> arguments_,
        string workingDirectory_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            executable_,
            nameof(executable_));

        ArgumentNullException.ThrowIfNull(
            arguments_,
            nameof(arguments_));

        ArgumentException.ThrowIfNullOrWhiteSpace(
            workingDirectory_,
            nameof(workingDirectory_));

        this.Executable =
            executable_;
        this.Arguments =
            arguments_;
        this.WorkingDirectory =
            workingDirectory_;
    }
}
