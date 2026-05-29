namespace AiRepoKit.Cli.Models;

public sealed record RepoDetection(
    string RepoRoot,
    string RecommendedProfile,
    double Confidence,
    IReadOnlyList<string> DetectedProfiles,
    IReadOnlyList<string> Signals,
    IReadOnlyList<string> Evidence,
    bool UsedFallbackProfile,
    string Warning);
