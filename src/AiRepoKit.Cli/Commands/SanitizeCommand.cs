using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class SanitizeCommand
{
    public CommandResult Execute(BootstrapOptions options_)
    {
        string repoRoot = Path.GetFullPath(options_.RepoPath);
        if (string.IsNullOrWhiteSpace(options_.SanitizeTerm))
        {
            return CommandResult.Failure("# Sanitize Error" + Environment.NewLine + Environment.NewLine + "`--term <term>` is required.", 1);
        }

        if (string.IsNullOrWhiteSpace(options_.SanitizeReplacement))
        {
            return CommandResult.Failure("# Sanitize Error" + Environment.NewLine + Environment.NewLine + "`--replacement <value>` is required.", 1);
        }

        ForbiddenTermScanner scanner = new();
        IReadOnlyList<ForbiddenTermFinding> findings = scanner.Scan(repoRoot, [options_.SanitizeTerm]);
        IReadOnlyList<string> replaceableFiles = scanner.GetReplaceableFiles(repoRoot, findings);
        bool apply = options_.Apply && !options_.DryRun;
        List<string> warnings = [];
        List<string> written = [];

        if (apply && !options_.Backup)
        {
            return CommandResult.Failure("# Sanitize Error" + Environment.NewLine + Environment.NewLine + "`sanitize --apply` requires `--backup`.", 1);
        }

        if (apply)
        {
            foreach (string relative in replaceableFiles)
            {
                string path = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    continue;
                }

                File.Copy(path, $"{path}.{DateTimeOffset.Now:yyyyMMddHHmmss}.bak", false);
                string content = File.ReadAllText(path);
                string updated = content.Replace(options_.SanitizeTerm, options_.SanitizeReplacement, StringComparison.OrdinalIgnoreCase);
                if (!string.Equals(content, updated, StringComparison.Ordinal))
                {
                    File.WriteAllText(path, updated);
                    written.Add(relative);
                }
            }
        }

        foreach (ForbiddenTermFinding finding in findings.Where(finding_ => !replaceableFiles.Contains(finding_.File, StringComparer.OrdinalIgnoreCase)))
        {
            warnings.Add($"{finding.File}:{finding.Line} is not a managed/generated file; manual review required.");
        }

        string markdown = WriteReport(options_, apply, findings, replaceableFiles, written, warnings);
        return CommandResult.Ok(markdown);
    }

    private static string WriteReport(
        BootstrapOptions options_,
        bool apply_,
        IReadOnlyList<ForbiddenTermFinding> findings_,
        IReadOnlyList<string> replaceableFiles_,
        IReadOnlyList<string> written_,
        IReadOnlyList<string> warnings_)
    {
        StringBuilder builder = new();
        builder.AppendLine(apply_ ? "# Sanitize Apply" : "# Sanitize Dry Run");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{Path.GetFullPath(options_.RepoPath)}`");
        builder.AppendLine($"- Term: `{ProcessRunner.Redact(options_.SanitizeTerm)}`");
        builder.AppendLine($"- Replacement: `{ProcessRunner.Redact(options_.SanitizeReplacement)}`");
        builder.AppendLine($"- Findings: `{findings_.Count}`");
        builder.AppendLine($"- Replaceable files: `{replaceableFiles_.Count}`");
        builder.AppendLine();
        builder.AppendLine(apply_ ? "## Files Written" : "## Files Planned");
        Append(builder, apply_ ? written_ : replaceableFiles_);
        builder.AppendLine();
        builder.AppendLine("## Findings");
        if (findings_.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (ForbiddenTermFinding finding in findings_.Take(200))
            {
                builder.AppendLine($"- `{finding.File}:{finding.Line}` generated=`{finding.GeneratedArtifact}` managed=`{finding.ManagedFile}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Warnings");
        Append(builder, warnings_);
        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine("- This command does not rewrite Git history, run git filter-repo, force-push, or edit unmanaged source files.");
        return builder.ToString().TrimEnd();
    }

    private static void Append(StringBuilder builder_, IReadOnlyList<string> values_)
    {
        if (values_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string value in values_)
        {
            builder_.AppendLine($"- `{ProcessRunner.Redact(value)}`");
        }
    }
}
