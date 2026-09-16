namespace AiRepoKit.Agents;

public sealed record ExecutionEnvironment
{
    public string WorkingDirectory
    {
        get;
    }

    public ExecutionEnvironment(
        string workingDirectory_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workingDirectory_,
            nameof(workingDirectory_));

        if (!Path.IsPathFullyQualified(
                workingDirectory_))
        {
            throw new ArgumentException(
                "Working directory path must be fully qualified.",
                nameof(workingDirectory_));
        }

        this.WorkingDirectory =
            workingDirectory_;
    }
}
