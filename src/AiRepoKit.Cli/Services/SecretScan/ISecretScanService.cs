namespace AiRepoKit.Cli.Services.SecretScan;

public interface ISecretScanService
{
    SecretScanRunResult Run(
        string repoRoot);
}
