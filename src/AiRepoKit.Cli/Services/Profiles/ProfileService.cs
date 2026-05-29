using AiRepoKit.Cli.Models.Profiles;

namespace AiRepoKit.Cli.Services.Profiles;

public sealed class ProfileService
{
    public const string DefaultProfileName = "generic";

    private static readonly IReadOnlyDictionary<string, ProfileDefinition> Definitions = CreateDefinitions();

    public IReadOnlyList<string> GetSupportedProfileNames()
    {
        return Definitions.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool IsSupported(string profileName_)
    {
        return Definitions.ContainsKey(NormalizeProfileName(profileName_));
    }

    public ProfileTemplateSelection GetSelection(string profileName_)
    {
        string normalized = NormalizeProfileName(profileName_);
        if (!Definitions.TryGetValue(normalized, out ProfileDefinition? definition))
        {
            definition = Definitions[DefaultProfileName];
        }

        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        List<ProfileDefinition> orderedDefinitions = [];
        AddWithBases(definition.Name, visited, orderedDefinitions);

        return new ProfileTemplateSelection(
            definition.Name,
            orderedDefinitions.Select(item_ => item_.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Merge(orderedDefinitions, item_ => item_.AgentTemplates),
            Merge(orderedDefinitions, item_ => item_.InstructionTemplates),
            Merge(orderedDefinitions, item_ => item_.PromptTemplates),
            Merge(orderedDefinitions, item_ => item_.RecommendedContextPackTasks),
            Merge(orderedDefinitions, item_ => item_.ValidationHints),
            Merge(orderedDefinitions, item_ => item_.RiskZones));
    }

    public string NormalizeProfileName(string? profileName_)
    {
        return string.IsNullOrWhiteSpace(profileName_)
            ? DefaultProfileName
            : profileName_.Trim().ToLowerInvariant();
    }

    private static void AddWithBases(string profileName_, HashSet<string> visited_, List<ProfileDefinition> orderedDefinitions_)
    {
        if (!Definitions.TryGetValue(profileName_, out ProfileDefinition? definition) || !visited_.Add(definition.Name))
        {
            return;
        }

        foreach (string includedProfile in definition.IncludedProfiles)
        {
            AddWithBases(includedProfile, visited_, orderedDefinitions_);
        }

        orderedDefinitions_.Add(definition);
    }

    private static IReadOnlyList<string> Merge(
        IReadOnlyList<ProfileDefinition> definitions_,
        Func<ProfileDefinition, IReadOnlyList<string>> selector_)
    {
        return definitions_
            .SelectMany(selector_)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, ProfileDefinition> CreateDefinitions()
    {
        ProfileDefinition generic = new(
            "generic",
            [],
            ["ask", "plan", "implementer", "reviewer", "test-fixer"],
            ["csharp", "tests", "safe-change"],
            ["review-risk", "fix-bug", "generate-tests"],
            ["review-risk", "fix-build", "test-generation"],
            ["Keep changes scoped.", "Use MCP context before broad file reads."],
            ["secrets", "generated outputs", "build scripts"]);

        ProfileDefinition dotnet = new(
            "dotnet",
            ["generic"],
            ["security-reviewer", "source-generator-specialist"],
            ["source-generator"],
            ["migration-plan", "analyze-source-generator"],
            ["security-review", "fix-build"],
            ["Preserve target frameworks, nullable settings, and package policy."],
            ["SDK configuration", "source generators", "package updates"]);

        ProfileDefinition aspnetCore = new(
            "aspnet-core",
            ["dotnet"],
            ["api-reviewer"],
            ["aspnet-core"],
            ["review-api-change"],
            ["change-api", "security-review"],
            ["Review authentication, authorization, routing, model binding, and middleware impact."],
            ["public APIs", "middleware", "auth", "configuration"]);

        ProfileDefinition legacyDotnet = new(
            "legacy-dotnet",
            ["dotnet"],
            ["migration-architect"],
            ["legacy-dotnet"],
            ["migration-plan"],
            ["fix-build", "review-risk"],
            ["Prefer compatibility-preserving migration steps."],
            ["project format", "binding redirects", "framework-specific APIs"]);

        ProfileDefinition winforms = new(
            "winforms",
            ["legacy-dotnet"],
            ["winforms-specialist"],
            ["winforms"],
            [],
            ["fix-build", "review-risk"],
            ["Preserve designer files and event wiring."],
            ["designer files", "resource files", "UI thread access"]);

        ProfileDefinition oracleDatalayer = new(
            "oracle-datalayer",
            ["dotnet"],
            ["datalayer-specialist"],
            ["oracle-datalayer"],
            ["review-datasource-flow"],
            ["review-risk", "security-review"],
            ["Do not run SQL, migrations, or database commands without explicit approval."],
            ["SQL generation", "connection strings", "transactions", "data access boundaries"]);

        ProfileDefinition demo = new(
            "demo",
            ["dotnet", "aspnet-core", "legacy-dotnet", "winforms"],
            ["api-reviewer", "migration-architect", "source-generator-specialist", "security-reviewer", "winforms-specialist"],
            ["demo", "source-generator", "aspnet-core", "legacy-dotnet", "winforms"],
            ["analyze-source-generator", "review-api-change", "migration-plan"],
            ["change-api", "fix-build", "review-risk", "security-review", "test-generation"],
            ["Use this profile as a broad demonstration model before tailoring guidance to a specific repository type."],
            ["generated code", "legacy interop", "API contracts", "configuration", "build scripts"]);

        ProfileDefinition[] definitions = [generic, dotnet, aspnetCore, legacyDotnet, winforms, oracleDatalayer, demo];
        return definitions.ToDictionary(item_ => item_.Name, StringComparer.OrdinalIgnoreCase);
    }
}
