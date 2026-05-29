using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Org;

namespace AiRepoKit.Cli.Services.Org;

public static class OrgScoringService
{
    public static OrgScore CalculateReadiness(RepoAnalysis analysis_, RepoDetection detection_, OrgRepositoryFootprint footprint_, OrgRepositoryHealth health_)
    {
        List<string> positive = [];
        List<string> negative = [];
        List<string> recommendations = [];
        int score = 0;

        Add(health_.Exists, 10, "repository path exists", "repository path is missing", positive, negative);
        Add(health_.HasGit, 10, ".git exists", ".git was not found", positive, negative);
        Add(health_.HasDetectableProject, 15, "solution/project or manifest detected", "no solution/project or manifest detected", positive, negative);
        Add(detection_.Confidence >= 0.70, 15, "profile confidence is high", "profile confidence is low", positive, negative);
        Add(footprint_.HasAiDirectory, 10, ".ai directory exists", ".ai directory is missing", positive, negative);
        Add(footprint_.HasMcpConfig, 10, "MCP config exists", "MCP config is missing", positive, negative);
        Add(footprint_.HasContextPacks, 15, "context packs exist", "context packs are missing", positive, negative);
        Add(footprint_.HasGraphs, 10, "graphs exist", "graphs are missing", positive, negative);
        Add(footprint_.HasToolManifest, 5, "tool manifest exists", "tool manifest is missing", positive, negative);

        if (!footprint_.HasAiDirectory)
        {
            recommendations.Add("Run `airepo setup --repo <repo>` to preview onboarding.");
        }

        if (!footprint_.HasContextPacks)
        {
            recommendations.Add("Generate context packs when ready with `airepo context-pack --apply`.");
        }

        if (!footprint_.HasGraphs)
        {
            recommendations.Add("Generate graph reports when ready with `airepo graph --apply`.");
        }

        return new OrgScore(Math.Clamp(score, 0, 100), Status(score), positive, negative, recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        void Add(bool condition_, int points_, string positive_, string negative_, List<string> positives_, List<string> negatives_)
        {
            if (condition_)
            {
                score += points_;
                positives_.Add(positive_);
            }
            else
            {
                negatives_.Add(negative_);
            }
        }
    }

    public static OrgScore CalculateCompliance(RepoAnalysis analysis_, RepoDetection detection_, OrgRepositoryFootprint footprint_, OrgRepositoryHealth health_)
    {
        List<string> positive = [];
        List<string> negative = [];
        List<string> recommendations = [];
        int score = 0;

        Add(footprint_.HasAgentsMd, 15, "AGENTS.md exists", "AGENTS.md is missing");
        Add(footprint_.HasCopilotInstructions, 15, ".github/copilot-instructions.md exists", ".github/copilot-instructions.md is missing");
        Add(footprint_.HasGithubAgents, 10, ".github/agents exists", ".github/agents is missing");
        Add(footprint_.HasGithubInstructions, 10, ".github/instructions exists", ".github/instructions is missing");
        Add(footprint_.HasGithubPrompts, 10, ".github/prompts exists", ".github/prompts is missing");
        Add(footprint_.HasVsCodeMcpConfig, 10, ".vscode/mcp.json exists", ".vscode/mcp.json is missing");
        Add(footprint_.HasCodexConfig, 10, ".codex/config.toml exists", ".codex/config.toml is missing");
        Add(footprint_.HasToolManifest, 10, "tool manifest exists", "tool manifest is missing");
        Add(!detection_.UsedFallbackProfile, 10, "profile detection did not require fallback", "profile detection used fallback");

        if (!footprint_.HasAgentsMd)
        {
            recommendations.Add("Add AGENTS.md before broad internal rollout.");
        }

        if (!footprint_.HasCopilotInstructions)
        {
            recommendations.Add("Add .github/copilot-instructions.md for Copilot guidance.");
        }

        if (!footprint_.HasMcpConfig)
        {
            recommendations.Add("Add MCP client config through setup when ready.");
        }

        return new OrgScore(Math.Clamp(score, 0, 100), Status(score), positive, negative, recommendations.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        void Add(bool condition_, int points_, string positive_, string negative_)
        {
            if (condition_)
            {
                score += points_;
                positive.Add(positive_);
            }
            else
            {
                negative.Add(negative_);
            }
        }
    }

    public static OrgScoreSummary Summarize(IEnumerable<OrgScore> scores_)
    {
        OrgScore[] scores = scores_.ToArray();
        if (scores.Length == 0)
        {
            return new OrgScoreSummary(0, 0, 0, 0, 0);
        }

        return new OrgScoreSummary(
            scores.Average(score_ => score_.Value),
            scores.Count(score_ => score_.Status.Equals("ok", StringComparison.OrdinalIgnoreCase)),
            scores.Count(score_ => score_.Status.Equals("warning", StringComparison.OrdinalIgnoreCase)),
            scores.Count(score_ => score_.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)),
            scores.Count(score_ => score_.Status.Equals("unknown", StringComparison.OrdinalIgnoreCase)));
    }

    private static string Status(int score_) => score_ >= 80 ? "ok" : score_ >= 50 ? "warning" : score_ > 0 ? "failed" : "unknown";
}
