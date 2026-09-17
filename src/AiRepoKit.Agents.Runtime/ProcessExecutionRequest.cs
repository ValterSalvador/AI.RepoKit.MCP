namespace AiRepoKit.Agents.Runtime;

public sealed record ProcessExecutionRequest
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

    public ProcessExecutionRequest(
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

        if (!Path.IsPathFullyQualified(workingDirectory_))
        {
            throw new ArgumentException(
                "Working directory must be a fully-qualified path.",
                nameof(workingDirectory_));
        }

        this.Executable =
            executable_;
        this.Arguments =
            Array.AsReadOnly(arguments_.ToArray());
        this.WorkingDirectory =
            workingDirectory_;
    }
}
