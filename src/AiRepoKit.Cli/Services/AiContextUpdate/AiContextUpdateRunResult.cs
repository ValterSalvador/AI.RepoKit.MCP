namespace AiRepoKit.Cli.Services.AiContextUpdate;

public sealed class AiContextUpdateRunResult
{
    private AiContextUpdateRunResult(
        bool isSuccess,
        string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public string? ErrorMessage { get; }

    public static AiContextUpdateRunResult Success()
    {
        return new AiContextUpdateRunResult(
            true,
            null);
    }

    public static AiContextUpdateRunResult Failure(
        string errorMessage)
    {
        return new AiContextUpdateRunResult(
            false,
            errorMessage);
    }
}
