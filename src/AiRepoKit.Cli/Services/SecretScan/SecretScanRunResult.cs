namespace AiRepoKit.Cli.Services.SecretScan;

public sealed class SecretScanRunResult
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public SecretScanReport? Report { get; init; }

    public static SecretScanRunResult Success(
        SecretScanReport report)
    {
        return new SecretScanRunResult
        {
            IsSuccess = true,
            Report = report
        };
    }

    public static SecretScanRunResult Failure(
        string errorMessage)
    {
        return new SecretScanRunResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
