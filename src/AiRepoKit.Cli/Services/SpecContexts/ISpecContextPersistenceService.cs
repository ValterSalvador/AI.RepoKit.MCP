using AiRepoKit.Spec.Context;

namespace AiRepoKit.Cli.Services.SpecContexts;

public interface ISpecContextPersistenceService
{
    string Persist(
        string repoRoot_,
        SpecContext specContext_);
}
