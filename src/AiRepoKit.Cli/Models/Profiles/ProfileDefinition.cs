namespace AiRepoKit.Cli.Models.Profiles;

public sealed record ProfileDefinition(
    string Name,
    IReadOnlyList<string> IncludedProfiles,
    IReadOnlyList<string> AgentTemplates,
    IReadOnlyList<string> InstructionTemplates,
    IReadOnlyList<string> PromptTemplates,
    IReadOnlyList<string> RecommendedContextPackTasks,
    IReadOnlyList<string> ValidationHints,
    IReadOnlyList<string> RiskZones);
