using AiRepoKit.Spec;

namespace AiRepoKit.Cli.Models.SpecContexts;

public sealed record SpecContextBuildRequest(
    string RepoRoot,
    string SpecId,
    RequirementSet RequirementSet,
    WorkSpec WorkSpec,
    string Target,
    int ReferenceLimit,
    int Budget,
    int MaxFiles = 10000);
