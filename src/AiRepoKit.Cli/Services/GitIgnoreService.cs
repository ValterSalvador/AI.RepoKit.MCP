using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class GitIgnoreService
{
    public const string SectionMarker = "# AiRepoKit local/generated artifacts";

    private static readonly string[] SectionRules =
    [
        ".ai/generated/",
        ".dotnet-home/",
        ".vs/",
        ".codex/config.toml",
        "Tools/AiContextMcp/bin/",
        "Tools/AiContextMcp/obj/",
        "artifacts/",
        ".tmp/",
        "/airepo.exe",
        "/airepo",
        "/install-ai-context.cmd",
        "/install-ai-context.ps1"
    ];

    public static string SectionContent => string.Join(Environment.NewLine, [SectionMarker, .. SectionRules]) + Environment.NewLine;

    public bool HasSection(string content_)
    {
        return content_.Contains(SectionMarker, StringComparison.OrdinalIgnoreCase);
    }

    public void EnsureSection(string rootPath_, BootstrapOptions options_)
    {
        string path = Path.Combine(Path.GetFullPath(rootPath_), ".gitignore");
        bool exists = File.Exists(path);
        string content = exists ? File.ReadAllText(path) : string.Empty;
        string updatedContent = GetUpdatedContent(content);
        if (string.Equals(content, updatedContent, StringComparison.Ordinal))
        {
            return;
        }

        if (exists && !options_.Backup && !options_.Force)
        {
            throw new InvalidOperationException("File exists and requires --backup or --force: .gitignore");
        }

        if (options_.DryRun)
        {
            return;
        }

        if (exists && options_.Backup)
        {
            File.Copy(path, $"{path}.{DateTimeOffset.Now:yyyyMMddHHmmss}.bak", false);
        }

        File.WriteAllText(path, updatedContent);
    }

    public bool EnsureLocalGeneratedArtifactRules(string rootPath_, bool dryRun_)
    {
        string path = Path.Combine(Path.GetFullPath(rootPath_), ".gitignore");
        string content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        string updatedContent = GetUpdatedContent(content);
        if (string.Equals(content, updatedContent, StringComparison.Ordinal))
        {
            return false;
        }

        if (!dryRun_)
        {
            File.WriteAllText(path, updatedContent);
        }

        return true;
    }

    private static string GetUpdatedContent(string content_)
    {
        if (string.IsNullOrWhiteSpace(content_))
        {
            return SectionContent;
        }

        string content = content_.TrimEnd();
        if (!content.Contains(SectionMarker, StringComparison.OrdinalIgnoreCase))
        {
            return content + Environment.NewLine + Environment.NewLine + SectionContent;
        }

        string[] lines = content.Split(["\r\n", "\n"], StringSplitOptions.None);
        HashSet<string> existingRules = lines
            .Select(line_ => line_.Trim())
            .Where(line_ => !string.IsNullOrWhiteSpace(line_))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] missingRules = SectionRules
            .Where(rule_ => !existingRules.Contains(rule_))
            .ToArray();
        if (missingRules.Length == 0)
        {
            return content_;
        }

        List<string> updatedLines = [];
        bool inserted = false;
        foreach (string line in lines)
        {
            updatedLines.Add(line);
            if (!inserted && line.Trim().Equals(SectionMarker, StringComparison.OrdinalIgnoreCase))
            {
                updatedLines.AddRange(missingRules);
                inserted = true;
            }
        }

        return string.Join(Environment.NewLine, updatedLines).TrimEnd() + Environment.NewLine;
    }
}
