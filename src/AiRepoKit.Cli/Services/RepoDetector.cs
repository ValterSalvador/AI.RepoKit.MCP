using AiRepoKit.Cli.Models;

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
        ".idea"
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
        bool hasPom = File.Exists(Path.Combine(rootPath, "pom.xml"));
        bool hasGradle = File.Exists(Path.Combine(rootPath, "build.gradle")) || File.Exists(Path.Combine(rootPath, "build.gradle.kts"));
        bool hasGoMod = File.Exists(Path.Combine(rootPath, "go.mod"));
        bool hasCargoToml = File.Exists(Path.Combine(rootPath, "Cargo.toml"));

        return new RepoAnalysis(
            profile,
            solutionFiles,
            projectFiles,
            DetectLanguages(solutionFiles, projectFiles, hasGlobalJson, hasPackageJson, hasTsConfig, hasPyProject, hasRequirements, hasPom, hasGradle, hasGoMod, hasCargoToml),
            DetectRepositoryTypes(solutionFiles, projectFiles, hasPackageJson, hasPyProject, hasRequirements, hasPom, hasGradle, hasGoMod, hasCargoToml),
            DetectRepositorySignals(solutionFiles, projectFiles, hasGlobalJson, hasPackageJson, hasTsConfig, hasPyProject, hasRequirements, hasPom, hasGradle, hasGoMod, hasCargoToml),
            hasGlobalJson,
            Directory.Exists(Path.Combine(rootPath, ".ai")),
            Directory.Exists(Path.Combine(rootPath, "Tools", "AiContext")),
            Directory.Exists(Path.Combine(rootPath, "Tools", "AiContextMcp")),
            File.Exists(Path.Combine(rootPath, ".codex", "config.toml")),
            File.Exists(Path.Combine(rootPath, ".vscode", "mcp.json")));
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
        bool hasPom_,
        bool hasGradle_,
        bool hasGoMod_,
        bool hasCargoToml_)
    {
        List<string> repositoryTypes = [];
        AddIf(repositoryTypes, solutionFiles_.Count > 0 || projectFiles_.Count > 0, "dotnet");
        AddIf(repositoryTypes, hasPackageJson_, "node");
        AddIf(repositoryTypes, hasPyProject_ || hasRequirements_, "python");
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
}
