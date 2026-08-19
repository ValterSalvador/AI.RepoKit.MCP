namespace AiRepoKit.Cli.Services.AiContextUpdate;

public interface IAiContextUpdateService
{
    AiContextUpdateRunResult Run(
        string repoRoot,
        AiContextUpdateOptions? options = null);
}
