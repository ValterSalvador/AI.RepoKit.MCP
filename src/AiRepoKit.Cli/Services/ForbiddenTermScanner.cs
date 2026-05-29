using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services.ManagedFiles;

namespace AiRepoKit.Cli.Services;

public sealed class ForbiddenTermScanner
{
    private static readonly string[] TextExtensions =
    [
        ".md",
        ".cs",
        ".ps1",
        ".sh",
        ".cmd",
        ".yml",
        ".yaml",
        ".json",
        ".toml",
        ".tpl",
        ".csproj",
        ".sln",
        ".slnx",
        ".props",
        ".targets",
        ".xml",
        ".config"
    ];

    private static readonly string[] IgnoredDirectories =
    [
        ".git",
        "bin",
        "obj",
        ".vs",
        "artifacts",
        "packages",
        "node_modules",
        ".tmp",
        ".dotnet-home"
    ];

    public IReadOnlyList<ForbiddenTermFinding> Scan(string repoRoot_, IReadOnlyList<string> terms_)
    {
        string repoRoot = Path.GetFullPath(repoRoot_);
        IReadOnlyList<string> terms = terms_.Where(term_ => !string.IsNullOrWhiteSpace(term_)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (terms.Count == 0 || !Directory.Exists(repoRoot))
        {
            return [];
        }

        HashSet<string> managedFiles = new(new ManagedFilesService().Load(repoRoot).Files.Select(file_ => file_.Path), StringComparer.OrdinalIgnoreCase);
        List<ForbiddenTermFinding> findings = [];
        foreach (string relative in EnumerateFiles(repoRoot))
        {
            string fullPath = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            string[] lines;
            try
            {
                lines = File.ReadAllLines(fullPath);
            }
            catch
            {
                continue;
            }

            for (int index = 0; index < lines.Length; index++)
            {
                foreach (string term in terms)
                {
                    if (lines[index].Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        findings.Add(new ForbiddenTermFinding(relative, index + 1, term, Limit(lines[index]), IsGeneratedArtifact(relative), managedFiles.Contains(relative)));
                    }
                }
            }
        }

        return findings;
    }

    public IReadOnlyList<string> GetReplaceableFiles(string repoRoot_, IReadOnlyList<ForbiddenTermFinding> findings_)
    {
        HashSet<string> managedFiles = new(new ManagedFilesService().Load(repoRoot_).Files.Select(file_ => file_.Path), StringComparer.OrdinalIgnoreCase);
        return findings_
            .Where(finding_ => finding_.GeneratedArtifact || managedFiles.Contains(finding_.File))
            .Select(finding_ => finding_.File)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateFiles(string repoRoot_)
    {
        IReadOnlyList<string> gitFiles = new GitService().GetVisibleFiles(repoRoot_);
        IEnumerable<string> candidates = gitFiles.Count > 0
            ? gitFiles
            : Directory.EnumerateFiles(repoRoot_, "*", SearchOption.AllDirectories)
                .Select(path_ => Path.GetRelativePath(repoRoot_, path_).Replace('\\', '/'));

        foreach (string relative in candidates)
        {
            string normalized = relative.Replace('\\', '/').TrimStart('/');
            if (IsIgnored(normalized) || !TextExtensions.Contains(Path.GetExtension(normalized), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return normalized;
        }
    }

    private static bool IsIgnored(string relativePath_)
    {
        string normalized = relativePath_.Replace('\\', '/').TrimStart('/');
        return IgnoredDirectories.Any(entry_ =>
            normalized.Equals(entry_, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(entry_ + "/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/" + entry_ + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGeneratedArtifact(string relativePath_)
    {
        return relativePath_.StartsWith(".ai/generated/", StringComparison.OrdinalIgnoreCase)
            || relativePath_.Equals(".codex/config.toml", StringComparison.OrdinalIgnoreCase);
    }

    private static string Limit(string value_)
    {
        string value = ProcessRunner.Redact(value_.Trim());
        return value.Length <= 160 ? value : value[..160];
    }
}
