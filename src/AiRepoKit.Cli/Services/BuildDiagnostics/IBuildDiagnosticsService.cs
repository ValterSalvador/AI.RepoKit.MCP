namespace AiRepoKit.Cli.Services.BuildDiagnostics;

public interface IBuildDiagnosticsService
{
    BuildDiagnosticsRunResult Run(
        string repoRoot);
}
