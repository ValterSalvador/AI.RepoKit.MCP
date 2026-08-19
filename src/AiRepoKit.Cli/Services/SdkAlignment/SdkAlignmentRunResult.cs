namespace AiRepoKit.Cli.Services.SdkAlignment;

public sealed class SdkAlignmentRunResult
{
    public bool IsSuccess { get; init; }

    public string? ErrorMessage { get; init; }

    public SdkAlignmentReport? Report { get; init; }

    public static SdkAlignmentRunResult Success(SdkAlignmentReport report)
    {
        return new SdkAlignmentRunResult
        {
            IsSuccess = true,
            Report = report
        };
    }

    public static SdkAlignmentRunResult Failure(string errorMessage)
    {
        return new SdkAlignmentRunResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}
