using System.Text.Json.Serialization;

namespace AiRepo.ContextMcp.Models;

public sealed record ContextManifest
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = "0.2";

    [JsonPropertyName("repoName")]
    public string RepoName { get; init; } = string.Empty;

    [JsonPropertyName("repoRoot")]
    public string RepoRoot { get; init; } = string.Empty;

    [JsonPropertyName("mainSolution")]
    public string MainSolution { get; init; } = string.Empty;

    [JsonPropertyName("allowedContextFiles")]
    public IReadOnlyList<string> AllowedContextFiles { get; init; } = [];

    [JsonPropertyName("restrictedPaths")]
    public IReadOnlyList<string> RestrictedPaths { get; init; } = [];

    [JsonPropertyName("budgets")]
    public ContextBudgetOptions Budgets { get; init; } = new();
}

public sealed record ContextBudgetOptions
{
    [JsonPropertyName("compactBytes")]
    public int CompactBytes { get; init; } = 8192;

    [JsonPropertyName("fullBytes")]
    public int FullBytes { get; init; } = 49152;

    [JsonPropertyName("combinedBytes")]
    public int CombinedBytes { get; init; } = 65536;

    [JsonPropertyName("fileReadBytes")]
    public int FileReadBytes { get; init; } = 1048576;

    [JsonPropertyName("searchDefaultLimit")]
    public int SearchDefaultLimit { get; init; } = 5;

    [JsonPropertyName("searchHardLimit")]
    public int SearchHardLimit { get; init; } = 25;

    [JsonPropertyName("arrayDefaultLimit")]
    public int ArrayDefaultLimit { get; init; } = 25;

    [JsonPropertyName("arrayHardLimit")]
    public int ArrayHardLimit { get; init; } = 100;

    [JsonPropertyName("previewChars")]
    public int PreviewChars { get; init; } = 240;
}
