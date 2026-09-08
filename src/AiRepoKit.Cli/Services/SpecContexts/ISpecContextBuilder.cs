using AiRepoKit.Cli.Models.SpecContexts;
using AiRepoKit.Spec.Context;

namespace AiRepoKit.Cli.Services.SpecContexts;

public interface ISpecContextBuilder
{
    SpecContext Build(
        SpecContextBuildRequest request_);
}
