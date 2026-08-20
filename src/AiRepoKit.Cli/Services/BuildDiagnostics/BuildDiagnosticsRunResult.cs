namespace AiRepoKit.Cli.Services.BuildDiagnostics;

public sealed class BuildDiagnosticsRunResult
{
    public bool Completed { get; init; }

    public int ExitCode { get; init; }

    public BuildDiagnosticsReport? Report { get; init; }

    public string? ErrorMessage { get; init; }

    public static BuildDiagnosticsRunResult Complete(
        BuildDiagnosticsReport report,
        int exitCode)
    {
        return new BuildDiagnosticsRunResult
        {
            Completed = true,
            ExitCode = exitCode,
            Report = report
        };
    }

    public static BuildDiagnosticsRunResult Failure(
        string errorMessage)
    {
        return new BuildDiagnosticsRunResult
        {
            Completed = false,
            ExitCode = 1,
            ErrorMessage = errorMessage
        };
    }
}
