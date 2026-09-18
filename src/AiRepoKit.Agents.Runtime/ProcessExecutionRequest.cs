namespace AiRepoKit.Agents.Runtime;

public sealed record ProcessExecutionRequest
{
    private const long MaxTimeoutMilliseconds = 4294967294L;

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

    public TimeSpan? Timeout
    {
        get;
    }

    public ProcessExecutionRequest(
        string executable_,
        IReadOnlyList<string> arguments_,
        string workingDirectory_,
        TimeSpan? timeout_ = null)
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

        if (timeout_.HasValue)
        {
            if (timeout_.Value <= TimeSpan.Zero || timeout_.Value.TotalMilliseconds > MaxTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeout_),
                    timeout_.Value,
                    $"Timeout must be greater than zero and less than or equal to {MaxTimeoutMilliseconds} milliseconds.");
            }
        }

        this.Executable =
            executable_;
        this.Arguments =
            Array.AsReadOnly(arguments_.ToArray());
        this.WorkingDirectory =
            workingDirectory_;
        this.Timeout =
            timeout_;
    }
}
