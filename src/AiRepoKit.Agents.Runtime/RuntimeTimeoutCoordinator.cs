namespace AiRepoKit.Agents.Runtime;

internal static class RuntimeTimeoutCoordinator
{
    public const long MaxTimeoutMilliseconds = 4294967294L;

    public static Exception ClassifyException(
        Exception exception_,
        CancellationToken callerToken_,
        bool timeoutExpired_,
        TimeSpan? timeout_)
    {
        if (callerToken_.IsCancellationRequested)
        {
            return new OperationCanceledException(
                "The operation was canceled by the caller.",
                exception_,
                callerToken_);
        }

        if (timeoutExpired_)
        {
            string timeoutMessage = timeout_.HasValue
                ? $"The operation timed out after {timeout_.Value.TotalMilliseconds} ms."
                : "The operation timed out.";

            return new TimeoutException(timeoutMessage, exception_);
        }

        return exception_;
    }

    public static Exception CreateTimeoutException(
        CancellationToken callerToken_,
        TimeSpan? timeout_,
        Exception? innerException_ = null)
    {
        if (callerToken_.IsCancellationRequested)
        {
            return new OperationCanceledException(
                "The streaming execution was canceled by the caller.",
                innerException_,
                callerToken_);
        }

        string timeoutMessage = timeout_.HasValue
            ? $"The streaming execution timed out after {timeout_.Value.TotalMilliseconds} ms."
            : "The streaming execution timed out.";

        return new TimeoutException(timeoutMessage, innerException_);
    }
}
