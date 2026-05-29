using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Org;

namespace AiRepoKit.Cli.Services.Org;

public sealed class OrgRepositoryScanner
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "artifacts",
        ".tmp",
        ".dotnet-home",
        "generated"
    };

    public OrgScanReport Scan(string rootPath_, int maxDepth_)
    {
        string root = Path.GetFullPath(rootPath_);
        List<OrgRepositorySummary> repositories = [];
        List<string> warnings = [];

        if (!Directory.Exists(root))
        {
            warnings.Add($"Root does not exist: {root}");
            return new OrgScanReport(root, Now(), Math.Max(0, maxDepth_), repositories, warnings);
        }

        RepoDetector detector = new();
        foreach (string candidate in this.FindRepositories(root, Math.Max(0, maxDepth_), warnings))
        {
            try
            {
                RepoAnalysis analysis = detector.Analyze(candidate);
                RepoDetection detection = detector.Detect(candidate);
                OrgRepositoryFootprint footprint = ProbeFootprint(candidate);
                OrgRepositoryHealth health = new(
                    GetHealthStatus(analysis, detection),
                    analysis.Profile.Exists,
                    Directory.Exists(Path.Combine(candidate, ".git")),
                    analysis.SolutionFiles.Count > 0 || analysis.ProjectFiles.Count > 0 || analysis.RepositoryTypes.Count > 0);
                OrgScore readiness = OrgScoringService.CalculateReadiness(analysis, detection, footprint, health);
                OrgScore compliance = OrgScoringService.CalculateCompliance(analysis, detection, footprint, health);
                List<string> repoWarnings = [];
                if (!string.IsNullOrWhiteSpace(detection.Warning))
                {
                    repoWarnings.Add(detection.Warning);
                }

                repoWarnings.AddRange(readiness.NegativeSignals);
                repositories.Add(new OrgRepositorySummary(
                    candidate,
                    new DirectoryInfo(candidate).Name,
                    detection.RecommendedProfile,
                    detection.Confidence,
                    detection.DetectedProfiles,
                    analysis.DetectedLanguages,
                    detection.Signals,
                    health,
                    footprint,
                    readiness,
                    compliance,
                    repoWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    readiness.Recommendations.Concat(compliance.Recommendations).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
            }
            catch (Exception exception)
            {
                warnings.Add($"{candidate}: {ProcessRunner.Redact(exception.Message)}");
            }
        }

        return new OrgScanReport(root, Now(), Math.Max(0, maxDepth_), repositories.OrderBy(repo_ => repo_.RepoRoot, StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
    }

    private IEnumerable<string> FindRepositories(string root_, int maxDepth_, List<string> warnings_)
    {
        Stack<(string Path, int Depth)> pending = new();
        pending.Push((root_, 0));
        while (pending.Count > 0)
        {
            (string current, int depth) = pending.Pop();
            if (IsRepositoryCandidate(current))
            {
                yield return Path.GetFullPath(current);
                continue;
            }

            if (depth >= maxDepth_)
            {
                continue;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly).ToArray();
            }
            catch (Exception exception)
            {
                warnings_.Add($"{current}: {ProcessRunner.Redact(exception.Message)}");
                continue;
            }

            foreach (string directory in directories.Reverse())
            {
                if (ShouldSkipDirectory(directory))
                {
                    continue;
                }

                pending.Push((directory, depth + 1));
            }
        }
    }

    private static bool IsRepositoryCandidate(string path_)
    {
        if (!Directory.Exists(path_))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(path_, ".git"))
            || Directory.EnumerateFiles(path_, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(path_, "*.slnx", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(path_, "*.csproj", SearchOption.TopDirectoryOnly).Any()
            || File.Exists(Path.Combine(path_, "package.json"))
            || File.Exists(Path.Combine(path_, "pyproject.toml"))
            || File.Exists(Path.Combine(path_, "composer.json"))
            || File.Exists(Path.Combine(path_, "pom.xml"))
            || File.Exists(Path.Combine(path_, "build.gradle"))
            || File.Exists(Path.Combine(path_, "build.gradle.kts"))
            || File.Exists(Path.Combine(path_, "go.mod"))
            || File.Exists(Path.Combine(path_, "Cargo.toml"));
    }

    private static bool ShouldSkipDirectory(string path_)
    {
        string name = Path.GetFileName(path_);
        if (IgnoredDirectories.Contains(name))
        {
            return true;
        }

        string normalized = path_.Replace('\\', '/');
        if (normalized.Contains("/.ai/generated/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(path_);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return true;
        }
    }

    private static OrgRepositoryFootprint ProbeFootprint(string repoRoot_)
    {
        bool hasVsCode = File.Exists(Path.Combine(repoRoot_, ".vscode", "mcp.json"));
        bool hasCodex = File.Exists(Path.Combine(repoRoot_, ".codex", "config.toml"));
        bool hasMcpProject = Directory.Exists(Path.Combine(repoRoot_, "Tools", "AiContextMcp"));
        return new OrgRepositoryFootprint(
            Directory.Exists(Path.Combine(repoRoot_, ".ai")),
            File.Exists(Path.Combine(repoRoot_, "AGENTS.md")),
            File.Exists(Path.Combine(repoRoot_, ".github", "copilot-instructions.md")),
            Directory.Exists(Path.Combine(repoRoot_, ".github", "agents")),
            Directory.Exists(Path.Combine(repoRoot_, ".github", "instructions")),
            Directory.Exists(Path.Combine(repoRoot_, ".github", "prompts")),
            hasVsCode,
            hasCodex,
            Directory.Exists(Path.Combine(repoRoot_, ".ai", "generated", "context-packs")) && Directory.EnumerateFiles(Path.Combine(repoRoot_, ".ai", "generated", "context-packs"), "*.json", SearchOption.TopDirectoryOnly).Any(),
            Directory.Exists(Path.Combine(repoRoot_, ".ai", "generated", "graphs")) && Directory.EnumerateFiles(Path.Combine(repoRoot_, ".ai", "generated", "graphs"), "*.json", SearchOption.TopDirectoryOnly).Any(),
            hasVsCode || hasCodex || hasMcpProject,
            File.Exists(Path.Combine(repoRoot_, ".ai", "manifests", "mcp-context-manifest.json")));
    }

    private static string GetHealthStatus(RepoAnalysis analysis_, RepoDetection detection_)
    {
        if (!analysis_.Profile.Exists)
        {
            return "failed";
        }

        if (detection_.UsedFallbackProfile || detection_.Confidence < 0.45)
        {
            return "warning";
        }

        return "ok";
    }

    private static string Now() => DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz");
}
