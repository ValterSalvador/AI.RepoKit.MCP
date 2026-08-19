namespace AiRepoKit.Cli.Services.SdkAlignment;

public interface ISdkAlignmentService
{
    SdkAlignmentRunResult Run(string repoRoot);
}
