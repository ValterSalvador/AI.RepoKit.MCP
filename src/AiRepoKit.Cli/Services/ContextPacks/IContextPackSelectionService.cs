using AiRepoKit.Cli.Models.ContextPacks;

namespace AiRepoKit.Cli.Services.ContextPacks;

public interface IContextPackSelectionService
{
    ContextPackSelectionResult Select(
        ContextPackRequest request_,
        string generatedAtLocal_);

    ContextPackInventoryCompatibility GetInventoryCompatibility(
        string repoRoot_);
}
