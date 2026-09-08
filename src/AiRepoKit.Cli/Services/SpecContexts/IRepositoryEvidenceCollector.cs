using AiRepoKit.Cli.Models.SpecContexts;

namespace AiRepoKit.Cli.Services.SpecContexts;

public interface IRepositoryEvidenceCollector
{
    RepositoryEvidenceCollection Collect(
        RepositoryEvidenceCollectionRequest request_);
}
