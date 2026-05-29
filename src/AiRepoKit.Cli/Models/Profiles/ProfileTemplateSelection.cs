namespace AiRepoKit.Cli.Models.Profiles;

public sealed record ProfileTemplateSelection(
    string ProfileName,
    IReadOnlyList<string> IncludedProfiles,
    IReadOnlyList<string> AgentTemplates,
    IReadOnlyList<string> InstructionTemplates,
    IReadOnlyList<string> PromptTemplates,
    IReadOnlyList<string> RecommendedContextPackTasks,
    IReadOnlyList<string> ValidationHints,
    IReadOnlyList<string> RiskZones);
