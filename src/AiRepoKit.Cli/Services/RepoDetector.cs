using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services.Profiles;

namespace AiRepoKit.Cli.Services;

public sealed class RepoDetector
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".vscode",
        ".idea",
        "node_modules",
        "packages",
        "artifacts",
        ".ai",
        ".tmp"
    };

    public RepoAnalysis Analyze(string repoPath_)
    {
        string rootPath = Path.GetFullPath(repoPath_);
        bool exists = Directory.Exists(rootPath);
        RepoProfile profile = new(rootPath, exists);

        if (!exists)
        {
            return new RepoAnalysis(profile, [], [], [], [], [], false, false, false, false, false, false);
        }

        IReadOnlyList<string> solutionFiles = Directory
            .EnumerateFiles(rootPath, "*.sln*", SearchOption.TopDirectoryOnly)
            .Where(path_ => HasExtension(path_, ".sln") || HasExtension(path_, ".slnx"))
            .Select(path_ => Path.GetRelativePath(rootPath, path_))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IReadOnlyList<string> projectFiles = EnumerateProjectFiles(rootPath)
            .Select(path_ => Path.GetRelativePath(rootPath, path_))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        bool hasGlobalJson = File.Exists(Path.Combine(rootPath, "global.json"));
        bool hasPackageJson = File.Exists(Path.Combine(rootPath, "package.json"));
        bool hasTsConfig = File.Exists(Path.Combine(rootPath, "tsconfig.json"));
        bool hasPyProject = File.Exists(Path.Combine(rootPath, "pyproject.toml"));
        bool hasRequirements = File.Exists(Path.Combine(rootPath, "requirements.txt"));
        bool hasComposer = File.Exists(Path.Combine(rootPath, "composer.json"));
        bool hasPom = File.Exists(Path.Combine(rootPath, "pom.xml"));
        bool hasGradle = File.Exists(Path.Combine(rootPath, "build.gradle")) || File.Exists(Path.Combine(rootPath, "build.gradle.kts"));
        bool hasGoMod = File.Exists(Path.Combine(rootPath, "go.mod"));
        bool hasCargoToml = File.Exists(Path.Combine(rootPath, "Cargo.toml"));

        return new RepoAnalysis(
            profile,
            solutionFiles,
            projectFiles,
            DetectLanguages(solutionFiles, projectFiles, hasGlobalJson, hasPackageJson, hasTsConfig, hasPyProject, hasRequirements, hasComposer, hasPom, hasGradle, hasGoMod, hasCargoToml),
            DetectRepositoryTypes(solutionFiles, projectFiles, hasPackageJson, hasPyProject, hasRequirements, hasComposer, hasPom, hasGradle, hasGoMod, hasCargoToml),
            DetectRepositorySignals(solutionFiles, projectFiles, hasGlobalJson, hasPackageJson, hasTsConfig, hasPyProject, hasRequirements, hasComposer, hasPom, hasGradle, hasGoMod, hasCargoToml),
            hasGlobalJson,
            Directory.Exists(Path.Combine(rootPath, ".ai")),
            Directory.Exists(Path.Combine(rootPath, "Tools", "AiContext")),
            Directory.Exists(Path.Combine(rootPath, "Tools", "AiContextMcp")),
            File.Exists(Path.Combine(rootPath, ".codex", "config.toml")),
            File.Exists(Path.Combine(rootPath, ".vscode", "mcp.json")));
    }

    public RepoDetection Detect(string repoPath_)
    {
        string rootPath = Path.GetFullPath(repoPath_);
        RepoAnalysis analysis = this.Analyze(rootPath);
        List<string> detectedProfiles = [];
        List<string> signals = [.. analysis.RepositorySignals];
        List<string> evidence = [];

        Dictionary<string, int> scores = new(StringComparer.OrdinalIgnoreCase)
        {
            ["generic"] = 10
        };

        void Add(string profile_, int score_, string evidence_)
        {
            scores[profile_] = scores.TryGetValue(profile_, out int existing) ? existing + score_ : score_;
            if (!detectedProfiles.Contains(profile_, StringComparer.OrdinalIgnoreCase))
            {
                detectedProfiles.Add(profile_);
            }

            signals.Add(profile_);
            evidence.Add(evidence_);
        }

        if (!analysis.Profile.Exists)
        {
            return new RepoDetection(rootPath, ProfileService.DefaultProfileName, 0, [], [], ["Repository path does not exist."], true, "Repository path does not exist; using generic profile.");
        }

        if (analysis.SolutionFiles.Count > 0 || analysis.ProjectFiles.Count > 0 || analysis.HasGlobalJson)
        {
            Add("dotnet", 45, ".NET solution/project/global.json signal detected.");
        }

        IReadOnlyList<string> files = EnumerateProbeFiles(rootPath, 600);
        foreach (string relative in files)
        {
            string fileName = Path.GetFileName(relative);
            string extension = Path.GetExtension(relative);
            string lower = relative.ToLowerInvariant();
            string content = ReadSmallFile(rootPath, relative);
            if (LooksLikeDetectorSource(content))
            {
                content = string.Empty;
            }

            if (fileName.Equals("packages.config", StringComparison.OrdinalIgnoreCase)
                || content.Contains("<TargetFrameworkVersion>", StringComparison.OrdinalIgnoreCase)
                || content.Contains("ToolsVersion=", StringComparison.OrdinalIgnoreCase)
                || content.Contains("net4", StringComparison.OrdinalIgnoreCase))
            {
                Add("legacy-dotnet", 35, $"{relative} indicates legacy .NET.");
            }

            if (content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase)
                || lower.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".cshtml", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".razor", StringComparison.OrdinalIgnoreCase))
            {
                Add("aspnet-core", 40, $"{relative} indicates ASP.NET Core.");
            }

            if (content.Contains("<UseWindowsForms>true</UseWindowsForms>", StringComparison.OrdinalIgnoreCase)
                || content.Contains("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase))
            {
                Add("winforms", 40, $"{relative} indicates WinForms.");
            }

            if (content.Contains("<UseWPF>true</UseWPF>", StringComparison.OrdinalIgnoreCase)
                || content.Contains("PresentationFramework", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                Add("wpf", 40, $"{relative} indicates WPF.");
            }

            if (content.Contains("Oracle.ManagedDataAccess", StringComparison.OrdinalIgnoreCase)
                || content.Contains("OracleConnection", StringComparison.OrdinalIgnoreCase)
                || lower.Contains("datalayer", StringComparison.OrdinalIgnoreCase))
            {
                Add("oracle-datalayer", 35, $"{relative} indicates Oracle/data layer.");
            }

            if (content.Contains("ISourceGenerator", StringComparison.OrdinalIgnoreCase)
                || content.Contains("IIncrementalGenerator", StringComparison.OrdinalIgnoreCase)
                || content.Contains("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase)
                || content.Contains("OutputItemType=\"Analyzer\"", StringComparison.OrdinalIgnoreCase))
            {
                Add("source-generator", 30, $"{relative} indicates source generator/analyzer.");
            }
        }

        if (analysis.ProjectFiles.Count >= 8 || File.Exists(Path.Combine(rootPath, "Directory.Build.props")))
        {
            Add("enterprise-dotnet", 25, "Large or centrally configured .NET repository signal detected.");
        }

        if (File.Exists(Path.Combine(rootPath, "package.json")))
        {
            signals.Add("package.json future-signal");
            evidence.Add("package.json detected as future JavaScript/TypeScript signal.");
        }

        if (File.Exists(Path.Combine(rootPath, "tsconfig.json")))
        {
            signals.Add("tsconfig.json future-signal");
            evidence.Add("tsconfig.json detected as future TypeScript signal.");
        }

        if (File.Exists(Path.Combine(rootPath, "pyproject.toml")) || File.Exists(Path.Combine(rootPath, "requirements.txt")))
        {
            signals.Add("python future-signal");
            evidence.Add("pyproject.toml/requirements.txt detected as future Python signal.");
        }

        if (File.Exists(Path.Combine(rootPath, "composer.json")))
        {
            signals.Add("composer.json future-signal");
            evidence.Add("composer.json detected as future PHP signal.");
        }

        KeyValuePair<string, int> winner = scores.OrderByDescending(item_ => item_.Value).ThenBy(item_ => item_.Key, StringComparer.OrdinalIgnoreCase).First();
        double confidence = Math.Min(0.98, winner.Value / 100.0);
        bool lowConfidence = winner.Key == "generic" || confidence < 0.45;
        string recommended = lowConfidence ? ProfileService.DefaultProfileName : winner.Key;
        string warning = lowConfidence ? "Low confidence detection; using generic profile." : string.Empty;
        if (!detectedProfiles.Contains("generic", StringComparer.OrdinalIgnoreCase))
        {
            detectedProfiles.Insert(0, "generic");
        }

        if (!detectedProfiles.Contains(recommended, StringComparer.OrdinalIgnoreCase))
        {
            detectedProfiles.Add(recommended);
        }

        return new RepoDetection(
            rootPath,
            recommended,
            confidence,
            detectedProfiles.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            signals.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            lowConfidence,
            warning);
    }

    private static IEnumerable<string> EnumerateProjectFiles(string rootPath_)
    {
        Stack<string> pending = new();
        pending.Push(rootPath_);

        while (pending.Count > 0)
        {
            string current = pending.Pop();

            foreach (string file in Directory.EnumerateFiles(current, "*.csproj", SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (string directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(directory);
                if (!IgnoredDirectories.Contains(name))
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static bool HasExtension(string path_, string extension_)
    {
        return string.Equals(Path.GetExtension(path_), extension_, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> DetectLanguages(
        IReadOnlyList<string> solutionFiles_,
        IReadOnlyList<string> projectFiles_,
        bool hasGlobalJson_,
        bool hasPackageJson_,
        bool hasTsConfig_,
        bool hasPyProject_,
        bool hasRequirements_,
        bool hasComposer_,
        bool hasPom_,
        bool hasGradle_,
        bool hasGoMod_,
        bool hasCargoToml_)
    {
        List<string> languages = [];
        AddIf(languages, solutionFiles_.Count > 0 || projectFiles_.Count > 0 || hasGlobalJson_, "C#");
        AddIf(languages, hasPackageJson_, "JavaScript");
        AddIf(languages, hasTsConfig_, "TypeScript");
        AddIf(languages, hasPyProject_ || hasRequirements_, "Python");
        AddIf(languages, hasComposer_, "PHP");
        AddIf(languages, hasPom_ || hasGradle_, "Java");
        AddIf(languages, hasGoMod_, "Go");
        AddIf(languages, hasCargoToml_, "Rust");
        return languages;
    }

    private static IReadOnlyList<string> DetectRepositoryTypes(
        IReadOnlyList<string> solutionFiles_,
        IReadOnlyList<string> projectFiles_,
        bool hasPackageJson_,
        bool hasPyProject_,
        bool hasRequirements_,
        bool hasComposer_,
        bool hasPom_,
        bool hasGradle_,
        bool hasGoMod_,
        bool hasCargoToml_)
    {
        List<string> repositoryTypes = [];
        AddIf(repositoryTypes, solutionFiles_.Count > 0 || projectFiles_.Count > 0, "dotnet");
        AddIf(repositoryTypes, hasPackageJson_, "node");
        AddIf(repositoryTypes, hasPyProject_ || hasRequirements_, "python");
        AddIf(repositoryTypes, hasComposer_, "php");
        AddIf(repositoryTypes, hasPom_ || hasGradle_, "java");
        AddIf(repositoryTypes, hasGoMod_, "go");
        AddIf(repositoryTypes, hasCargoToml_, "rust");
        return repositoryTypes;
    }

    private static IReadOnlyList<string> DetectRepositorySignals(
        IReadOnlyList<string> solutionFiles_,
        IReadOnlyList<string> projectFiles_,
        bool hasGlobalJson_,
        bool hasPackageJson_,
        bool hasTsConfig_,
        bool hasPyProject_,
        bool hasRequirements_,
        bool hasComposer_,
        bool hasPom_,
        bool hasGradle_,
        bool hasGoMod_,
        bool hasCargoToml_)
    {
        List<string> signals = [];
        AddIf(signals, solutionFiles_.Count > 0, $"{solutionFiles_.Count} solution file(s)");
        AddIf(signals, projectFiles_.Count > 0, $"{projectFiles_.Count} C# project file(s)");
        AddIf(signals, hasGlobalJson_, "global.json");
        AddIf(signals, hasPackageJson_, "package.json");
        AddIf(signals, hasTsConfig_, "tsconfig.json");
        AddIf(signals, hasPyProject_, "pyproject.toml");
        AddIf(signals, hasRequirements_, "requirements.txt");
        AddIf(signals, hasComposer_, "composer.json");
        AddIf(signals, hasPom_, "pom.xml");
        AddIf(signals, hasGradle_, "build.gradle");
        AddIf(signals, hasGoMod_, "go.mod");
        AddIf(signals, hasCargoToml_, "Cargo.toml");
        return signals;
    }

    private static void AddIf(List<string> values_, bool condition_, string value_)
    {
        if (condition_ && !values_.Contains(value_, StringComparer.OrdinalIgnoreCase))
        {
            values_.Add(value_);
        }
    }

    private static IReadOnlyList<string> EnumerateProbeFiles(string rootPath_, int limit_)
    {
        IReadOnlyList<string> gitFiles = new GitService().GetVisibleFiles(rootPath_);
        IEnumerable<string> candidates = gitFiles.Count > 0
            ? gitFiles
            : Directory.EnumerateFiles(rootPath_, "*", SearchOption.AllDirectories)
                .Select(path_ => Path.GetRelativePath(rootPath_, path_).Replace('\\', '/'));

        return candidates
            .Where(path_ => !IsIgnoredPath(path_))
            .Where(path_ => IsProbeFile(path_))
            .Take(limit_)
            .ToArray();
    }

    private static bool IsProbeFile(string relativePath_)
    {
        string extension = Path.GetExtension(relativePath_);
        string fileName = Path.GetFileName(relativePath_);
        return extension is ".cs" or ".csproj" or ".vbproj" or ".fsproj" or ".props" or ".targets" or ".config" or ".xaml" or ".razor" or ".cshtml"
            || fileName.Equals("packages.config", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredPath(string relativePath_)
    {
        string normalized = relativePath_.Replace('\\', '/').TrimStart('/');
        return IgnoredDirectories.Any(entry_ =>
            normalized.Equals(entry_, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(entry_ + "/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/" + entry_ + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadSmallFile(string rootPath_, string relativePath_)
    {
        try
        {
            string path = Path.Combine(rootPath_, relativePath_.Replace('/', Path.DirectorySeparatorChar));
            FileInfo info = new(path);
            if (!info.Exists || info.Length > 256 * 1024)
            {
                return string.Empty;
            }

            return File.ReadAllText(path);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeDetectorSource(string content_)
    {
        return content_.Contains("public sealed class RepoDetector", StringComparison.Ordinal)
            || content_.Contains("DetectRepositorySignals", StringComparison.Ordinal);
    }
}
