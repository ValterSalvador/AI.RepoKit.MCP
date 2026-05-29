using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.Audit;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class AuditCommand
{
    public const string BaselineRelativePath = ".ai/policies/audit-baseline.json";

    private const string BaselineVersion = "1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly string[] AllowedExtensions =
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
        ".slnx"
    ];

    private static readonly string[] IgnoredDirectories =
    [
        ".git",
        ".ai/generated",
        ".dotnet-home",
        "bin",
        "obj",
        "artifacts",
        ".tmp",
        "node_modules",
        ".vs",
        ".idea"
    ];

    private static readonly string[] PilotNames =
    [
        "neo" + "_" + "v",
        "Val" + "ter",
        "AI.RepoKit" + "Sandbox",
        "AiRepoKit" + "Sandbox",
        "SamplePath" + "Bug",
        "RoslynLite" + "Sandbox",
        "RoslynLite" + "Validation",
        "ExeOnly" + "Sandbox",
        "DoubleClick" + "Sandbox",
        "Hygiene" + "Sandbox"
    ];

    private static readonly string[] PortugueseTerms =
    [
        "su" + "cesso",
        "er" + "ro",
        "avi" + "so",
        "valida" + "ção",
        "valida" + "cao",
        "reposit" + "ório",
        "reposi" + "torio",
        "arqui" + "vo",
        "se" + "nha",
        "cami" + "nho",
        "execu" + "tar",
        "conclu" + "ído",
        "conclu" + "ido",
        "fa" + "lhou",
        "nen" + "hum"
    ];

    private static readonly Regex LocalPathRegex = new(@"(?i)\b[A-Z]:\\(?:Users|Repositories)\\[^\s""'<>|]+|/(?:Users|home)/(?!user(?:/|$))[^/\s]+/[^\s""'<>|]+", RegexOptions.Compiled);
    private static readonly Regex SecretRegex = new(@"(?i)\b(password|passwd|pwd|secret|token|api[_-]?key|apikey|connectionstring|private[_-]?key|clientsecret)\b\s*[:=]\s*[""']?([^;,\s""'{}\]]+)", RegexOptions.Compiled);
    private static readonly Regex JsonSecretRegex = new(@"(?i)[""'](password|passwd|pwd|secret|token|api[_-]?key|apikey|connectionstring|private[_-]?key|clientsecret)[""']\s*:\s*[""']([^""'\r\n]+)[""']", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public CommandResult Execute(BootstrapOptions options_)
    {
        using ProgressReporter progress = ProgressReporter.Create(options_);
        try
        {
            progress.StartPhase("Scanning files");
            string repoRoot = Path.GetFullPath(options_.RepoPath);
            if (!Directory.Exists(repoRoot))
            {
                progress.FailPhase("Repository check failed");
                return CommandResult.Failure("# Audit Error" + Environment.NewLine + Environment.NewLine + "Repository path was not found.", 1);
            }

            string baselinePath = GetBaselinePath(repoRoot);
            if (options_.CreateAuditBaseline && File.Exists(baselinePath) && !options_.UpdateAuditBaseline)
            {
                progress.FailPhase("Baseline check failed");
                return CommandResult.Failure($"# Audit Error{Environment.NewLine}{Environment.NewLine}Audit baseline already exists at `{BaselineRelativePath}`. Use `--update-baseline` to merge current findings.", 1);
            }

            IReadOnlyList<AuditFinding> findings = this.Scan(repoRoot);
            progress.CompletePhase("File scanning completed");

            progress.StartPhase("Loading baseline");
            AuditBaseline? baseline = LoadBaseline(baselinePath);
            progress.CompletePhase("Baseline loading completed");
            string? baselineAction = null;
            if (options_.CreateAuditBaseline || options_.UpdateAuditBaseline)
            {
                progress.StartPhase("Writing audit baseline");
                baseline = WriteBaseline(baselinePath, baseline, findings, out baselineAction);
                progress.CompletePhase("Audit baseline writing completed");
            }

            progress.StartPhase("Evaluating findings");
            AuditEvaluationResult evaluation = EvaluateFindings(findings, baseline, options_.FailOnAccepted);
            progress.CompletePhase("Finding evaluation completed");
            bool includeBaselineSummary = options_.ShowAuditBaseline || options_.CreateAuditBaseline || options_.UpdateAuditBaseline || baseline is not null;
            string output = options_.AuditJson
                ? WriteJson(repoRoot, evaluation, includeBaselineSummary, baselineAction)
                : WriteMarkdown(repoRoot, evaluation, options_.Verbose, includeBaselineSummary, baselineAction);
            bool success = evaluation.Summary.ActiveHighSeverity == 0;
            if (success)
            {
                progress.CompletePhase("Audit completed");
            }
            else
            {
                progress.WarnPhase("Audit completed with findings");
            }
            return new CommandResult(success, output, success ? 0 : 2);
        }
        catch (InvalidDataException exception)
        {
            progress.FailPhase("Audit failed");
            return CommandResult.Failure("# Audit Error" + Environment.NewLine + Environment.NewLine + ProcessRunner.Redact(exception.Message), 1);
        }
        catch (Exception exception)
        {
            progress.FailPhase("Audit failed");
            return CommandResult.Failure("# Audit Error" + Environment.NewLine + Environment.NewLine + ProcessRunner.Redact(exception.Message), 1);
        }
    }

    private IReadOnlyList<AuditFinding> Scan(string repoRoot_)
    {
        List<AuditFinding> findings = [];
        foreach (string file in this.EnumerateFiles(repoRoot_))
        {
            string relative = Path.GetRelativePath(repoRoot_, file).Replace('\\', '/');
            if (IsGeneratedArtifact(relative))
            {
                findings.Add(CreateFinding(relative, "GeneratedArtifact", 1, "Generated or local artifact candidate.", "Warning"));
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(file);
            }
            catch
            {
                findings.Add(CreateFinding(relative, "Warning", 1, "File could not be read.", "Warning"));
                continue;
            }

            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int lineNumber = index + 1;
                this.AddLocalPathFindings(findings, relative, lineNumber, line);
                this.AddPilotNameFindings(findings, relative, lineNumber, line);
                this.AddSecretFindings(findings, relative, lineNumber, line);
                this.AddEmailFindings(findings, relative, lineNumber, line);
                this.AddPortugueseFindings(findings, relative, lineNumber, line);
            }
        }

        return findings
            .OrderByDescending(finding_ => SeverityRank(finding_.Severity))
            .ThenBy(finding_ => finding_.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding_ => finding_.Line)
            .ThenBy(finding_ => finding_.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<string> EnumerateFiles(string repoRoot_)
    {
        IReadOnlyList<string> gitFiles = GetGitVisibleFiles(repoRoot_);
        if (gitFiles.Count > 0)
        {
            foreach (string file in gitFiles)
            {
                string extension = Path.GetExtension(file);
                if (AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            yield break;
        }

        Stack<string> pending = new();
        pending.Push(repoRoot_);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string directory in Directory.EnumerateDirectories(current))
            {
                if (!IsIgnoredDirectory(repoRoot_, directory))
                {
                    pending.Push(directory);
                }
            }

            foreach (string file in Directory.EnumerateFiles(current))
            {
                string extension = Path.GetExtension(file);
                if (AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private static IReadOnlyList<string> GetGitVisibleFiles(string repoRoot_)
    {
        if (!Directory.Exists(Path.Combine(repoRoot_, ".git")))
        {
            return [];
        }

        try
        {
            using System.Diagnostics.Process process = new();
            process.StartInfo.FileName = "git";
            process.StartInfo.ArgumentList.Add("ls-files");
            process.StartInfo.ArgumentList.Add("--cached");
            process.StartInfo.ArgumentList.Add("--others");
            process.StartInfo.ArgumentList.Add("--exclude-standard");
            process.StartInfo.WorkingDirectory = repoRoot_;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            if (process.ExitCode != 0)
            {
                return [];
            }

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path_ => Path.GetFullPath(Path.Combine(repoRoot_, path_.Replace('/', Path.DirectorySeparatorChar))))
                .Where(File.Exists)
                .Where(path_ => !IsIgnoredDirectory(repoRoot_, Path.GetDirectoryName(path_) ?? repoRoot_))
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static bool IsIgnoredDirectory(string repoRoot_, string directory_)
    {
        string relative = Path.GetRelativePath(repoRoot_, directory_).Replace('\\', '/').Trim('/');
        string name = Path.GetFileName(directory_);
        return IgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
            || IgnoredDirectories.Any(ignored_ => relative.Equals(ignored_, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(ignored_ + "/", StringComparison.OrdinalIgnoreCase));
    }

    private void AddLocalPathFindings(List<AuditFinding> findings_, string file_, int line_, string text_)
    {
        foreach (Match match in LocalPathRegex.Matches(text_))
        {
            findings_.Add(CreateFinding(file_, "LocalPath", line_, RedactPreview(text_, match.Value), "High"));
        }
    }

    private void AddPilotNameFindings(List<AuditFinding> findings_, string file_, int line_, string text_)
    {
        foreach (string name in PilotNames)
        {
            if (text_.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                findings_.Add(CreateFinding(file_, "PilotName", line_, RedactPreview(text_, name), "High"));
                return;
            }
        }
    }

    private void AddSecretFindings(List<AuditFinding> findings_, string file_, int line_, string text_)
    {
        if (LooksLikeDetectorSource(text_))
        {
            return;
        }

        HashSet<string> previewHashes = new(StringComparer.OrdinalIgnoreCase);
        this.AddSecretFindings(findings_, file_, line_, text_, SecretRegex, previewHashes);
        this.AddSecretFindings(findings_, file_, line_, text_, JsonSecretRegex, previewHashes);
    }

    private void AddSecretFindings(List<AuditFinding> findings_, string file_, int line_, string text_, Regex regex_, HashSet<string> previewHashes_)
    {
        foreach (Match match in regex_.Matches(text_))
        {
            string value = match.Groups[2].Value.Trim();
            if (string.IsNullOrWhiteSpace(value) || IsPlaceholderSecretValue(value))
            {
                continue;
            }

            string preview = RedactPreview(text_, value);
            if (!previewHashes_.Add(ComputePreviewHash(preview)))
            {
                continue;
            }

            findings_.Add(CreateFinding(file_, "SecretPattern", line_, preview, "High"));
        }
    }

    private void AddEmailFindings(List<AuditFinding> findings_, string file_, int line_, string text_)
    {
        Match match = EmailRegex.Match(text_);
        if (!match.Success)
        {
            return;
        }

        if (match.Value.EndsWith("@example.com", StringComparison.OrdinalIgnoreCase)
            || match.Value.EndsWith("@example.org", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        findings_.Add(CreateFinding(file_, "SecretPattern", line_, RedactPreview(text_, match.Value), "High"));
    }

    private void AddPortugueseFindings(List<AuditFinding> findings_, string file_, int line_, string text_)
    {
        foreach (string term in PortugueseTerms)
        {
            if (Regex.IsMatch(text_, $@"(?i)(?<![A-Za-zÀ-ÿ]){Regex.Escape(term)}(?![A-Za-zÀ-ÿ])"))
            {
                findings_.Add(CreateFinding(file_, "PortugueseText", line_, LimitPreview(text_), "Warning"));
                return;
            }
        }
    }

    private static bool IsGeneratedArtifact(string relativePath_)
    {
        return relativePath_.StartsWith(".ai/generated/", StringComparison.OrdinalIgnoreCase)
            || relativePath_.StartsWith(".dotnet-home/", StringComparison.OrdinalIgnoreCase)
            || relativePath_.StartsWith(".ai/build-diagnostics/", StringComparison.OrdinalIgnoreCase)
            || relativePath_.StartsWith(".ai/security/", StringComparison.OrdinalIgnoreCase)
            || IsLegacyAiGeneratedReport(relativePath_)
            || relativePath_.Equals(".codex/config.toml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyAiGeneratedReport(string relativePath_)
    {
        if (!relativePath_.StartsWith(".ai/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = Path.GetFileName(relativePath_);
        return MatchesLegacyAiReport(fileName, "-inventory.json")
            || MatchesLegacyAiReport(fileName, "-inventory.md")
            || MatchesLegacyAiReport(fileName, "-report.json")
            || MatchesLegacyAiReport(fileName, "-report.md")
            || fileName.Equals("mcp-budget-report.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("mcp-budget-report.md", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLegacyAiReport(string fileName_, string suffix_)
    {
        return fileName_.EndsWith(suffix_, StringComparison.OrdinalIgnoreCase)
            && fileName_.Length > suffix_.Length;
    }

    private static bool IsPlaceholderSecretValue(string value_)
    {
        return value_.Length < 8
            || value_.Contains("<", StringComparison.Ordinal)
            || value_.Contains(">", StringComparison.Ordinal)
            || value_.Equals("todo", StringComparison.OrdinalIgnoreCase)
            || value_.Contains("redacted", StringComparison.OrdinalIgnoreCase)
            || value_.Contains("example", StringComparison.OrdinalIgnoreCase)
            || value_.Contains("placeholder", StringComparison.OrdinalIgnoreCase)
            || value_.Contains("***", StringComparison.Ordinal);
    }

    private static bool LooksLikeDetectorSource(string text_)
    {
        return text_.Contains("SecretRegex", StringComparison.Ordinal)
            || text_.Contains("patterns", StringComparison.OrdinalIgnoreCase)
            || text_.Contains("password\\s", StringComparison.OrdinalIgnoreCase)
            || text_.Contains("connectionstring\\s", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactPreview(string line_, string value_)
    {
        return LimitPreview(line_.Replace(value_, "<redacted>", StringComparison.OrdinalIgnoreCase));
    }

    private static string LimitPreview(string value_)
    {
        string normalized = ProcessRunner.Redact(value_.Trim());
        if (normalized.Length <= 160)
        {
            return normalized;
        }

        return normalized[..160];
    }

    private static int SeverityRank(string severity_)
    {
        return severity_.Equals("High", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private static string GetBaselinePath(string repoRoot_)
    {
        return Path.Combine(repoRoot_, BaselineRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static AuditFinding CreateFinding(string file_, string category_, int line_, string preview_, string severity_)
    {
        string normalizedFile = NormalizeRelativePath(file_);
        return new AuditFinding(normalizedFile, category_, line_, preview_, severity_, ComputePreviewHash(preview_));
    }

    private static string ComputePreviewHash(string preview_)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(preview_));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static AuditBaseline? LoadBaseline(string baselinePath_)
    {
        if (!File.Exists(baselinePath_))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(baselinePath_);
            AuditBaseline? baseline = JsonSerializer.Deserialize<AuditBaseline>(stream, JsonOptions);
            if (baseline is null)
            {
                throw new InvalidDataException($"Audit baseline file `{BaselineRelativePath}` is empty or invalid.");
            }

            return NormalizeBaseline(baseline);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Audit baseline file `{BaselineRelativePath}` is corrupt. {ProcessRunner.Redact(exception.Message)}", exception);
        }
        catch (IOException exception)
        {
            throw new InvalidDataException($"Audit baseline file `{BaselineRelativePath}` could not be read. {ProcessRunner.Redact(exception.Message)}", exception);
        }
    }

    private static AuditBaseline NormalizeBaseline(AuditBaseline baseline_)
    {
        if (string.IsNullOrWhiteSpace(baseline_.Version))
        {
            throw new InvalidDataException($"Audit baseline file `{BaselineRelativePath}` is missing `Version`.");
        }

        List<AuditBaselineEntry> entries = [];
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (AuditBaselineEntry entry in baseline_.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)
                || string.IsNullOrWhiteSpace(entry.Category)
                || string.IsNullOrWhiteSpace(entry.File)
                || string.IsNullOrWhiteSpace(entry.PreviewHash)
                || entry.Line <= 0
                || entry.CreatedAtLocal == default
                || entry.UpdatedAtLocal == default
                || !Enum.IsDefined(entry.Status))
            {
                throw new InvalidDataException($"Audit baseline file `{BaselineRelativePath}` contains an invalid entry.");
            }

            string normalizedFile = NormalizeRelativePath(entry.File);
            string key = GetBaselineKey(entry.Category, normalizedFile, entry.PreviewHash);
            if (!keys.Add(key))
            {
                throw new InvalidDataException($"Audit baseline file `{BaselineRelativePath}` contains duplicate entries.");
            }

            entries.Add(new AuditBaselineEntry(
                entry.Id,
                entry.Category,
                normalizedFile,
                entry.Line,
                entry.PreviewHash.ToLowerInvariant(),
                entry.Status,
                entry.Reason ?? string.Empty,
                entry.ExpiresOn,
                entry.CreatedAtLocal,
                entry.UpdatedAtLocal));
        }

        return new AuditBaseline(baseline_.Version, baseline_.GeneratedAtLocal, SortEntries(entries));
    }

    private static AuditBaseline WriteBaseline(string baselinePath_, AuditBaseline? existingBaseline_, IReadOnlyList<AuditFinding> findings_, out string? baselineAction_)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        Dictionary<string, AuditBaselineEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        if (existingBaseline_ is not null)
        {
            foreach (AuditBaselineEntry entry in existingBaseline_.Entries)
            {
                entries[GetBaselineKey(entry.Category, entry.File, entry.PreviewHash)] = entry;
            }
        }

        int added = 0;
        foreach (AuditFinding finding in findings_)
        {
            string key = GetBaselineKey(finding.Category, finding.File, finding.PreviewHash);
            if (entries.ContainsKey(key))
            {
                continue;
            }

            entries[key] = new AuditBaselineEntry(
                CreateBaselineEntryId(finding),
                finding.Category,
                finding.File,
                finding.Line,
                finding.PreviewHash,
                AuditBaselineStatus.ReviewRequired,
                string.Empty,
                null,
                now,
                now);
            added++;
        }

        AuditBaseline baseline = new(existingBaseline_?.Version ?? BaselineVersion, now, SortEntries(entries.Values));
        string? directory = Path.GetDirectoryName(baselinePath_);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidDataException("Audit baseline directory could not be resolved.");
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(baselinePath_, JsonSerializer.Serialize(baseline, JsonOptions));

        baselineAction_ = existingBaseline_ is null
            ? $"Created `{BaselineRelativePath}` with `{baseline.Entries.Count}` review-required entries."
            : $"Updated `{BaselineRelativePath}` with `{added}` new review-required entries.";

        return baseline;
    }

    private static AuditEvaluationResult EvaluateFindings(IReadOnlyList<AuditFinding> findings_, AuditBaseline? baseline_, bool failOnAccepted_)
    {
        Dictionary<string, AuditBaselineEntry> entries = new(StringComparer.OrdinalIgnoreCase);
        if (baseline_ is not null)
        {
            foreach (AuditBaselineEntry entry in baseline_.Entries)
            {
                entries[GetBaselineKey(entry.Category, entry.File, entry.PreviewHash)] = entry;
            }
        }

        List<AuditFinding> evaluated = [];
        foreach (AuditFinding finding in findings_)
        {
            string key = GetBaselineKey(finding.Category, finding.File, finding.PreviewHash);
            AuditBaselineStatus status = AuditBaselineStatus.ReviewRequired;
            bool isExpired = false;
            if (entries.TryGetValue(key, out AuditBaselineEntry? entry))
            {
                status = entry.Status;
                isExpired = IsExpired(entry);
            }

            evaluated.Add(finding with
            {
                BaselineStatus = status,
                IsExpired = isExpired
            });
        }

        AuditSummary summary = new(
            evaluated.Count,
            evaluated.Count(IsHighSeverity),
            evaluated.Count(finding_ => IsBlockingHighSeverity(finding_, failOnAccepted_)),
            evaluated.Count(finding_ => finding_.BaselineStatus == AuditBaselineStatus.Accepted && !finding_.IsExpired),
            evaluated.Count(finding_ => finding_.BaselineStatus == AuditBaselineStatus.FalsePositive && !finding_.IsExpired),
            evaluated.Count(finding_ => finding_.BaselineStatus == AuditBaselineStatus.ReviewRequired),
            baseline_?.Entries.Count(IsExpired) ?? 0,
            baseline_?.Entries.Count ?? 0,
            baseline_?.Entries.Count(entry_ => entry_.Status == AuditBaselineStatus.Accepted) ?? 0,
            baseline_?.Entries.Count(entry_ => entry_.Status == AuditBaselineStatus.FalsePositive) ?? 0,
            baseline_?.Entries.Count(entry_ => entry_.Status == AuditBaselineStatus.ReviewRequired) ?? 0);

        return new AuditEvaluationResult(evaluated, summary, baseline_, BaselineRelativePath);
    }

    private static bool IsHighSeverity(AuditFinding finding_)
    {
        return string.Equals(finding_.Severity, "High", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockingHighSeverity(AuditFinding finding_, bool failOnAccepted_)
    {
        if (!IsHighSeverity(finding_))
        {
            return false;
        }

        return finding_.BaselineStatus switch
        {
            AuditBaselineStatus.Accepted or AuditBaselineStatus.FalsePositive => finding_.IsExpired || failOnAccepted_,
            _ => true
        };
    }

    private static bool IsExpired(AuditBaselineEntry entry_)
    {
        if ((entry_.Status != AuditBaselineStatus.Accepted && entry_.Status != AuditBaselineStatus.FalsePositive)
            || entry_.ExpiresOn is null)
        {
            return false;
        }

        return entry_.ExpiresOn.Value < DateOnly.FromDateTime(DateTime.Now);
    }

    private static string CreateBaselineEntryId(AuditFinding finding_)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{finding_.Category}|{finding_.File}|{finding_.PreviewHash}"));
        return $"finding-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string GetBaselineKey(string category_, string file_, string previewHash_)
    {
        return $"{category_}|{NormalizeRelativePath(file_)}|{previewHash_.ToLowerInvariant()}";
    }

    private static IReadOnlyList<AuditBaselineEntry> SortEntries(IEnumerable<AuditBaselineEntry> entries_)
    {
        return entries_
            .OrderBy(entry_ => entry_.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry_ => entry_.Line)
            .ThenBy(entry_ => entry_.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry_ => entry_.PreviewHash, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeRelativePath(string path_)
    {
        string normalized = path_.Replace('\\', '/').Trim();
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static string WriteJson(string repoRoot_, AuditEvaluationResult evaluation_, bool includeBaselineSummary_, string? baselineAction_)
    {
        return JsonSerializer.Serialize(new
        {
            repo = repoRoot_,
            findingCount = evaluation_.Summary.Findings,
            highSeverityCount = evaluation_.Summary.HighSeverity,
            activeHighSeverityCount = evaluation_.Summary.ActiveHighSeverity,
            acceptedFindingCount = evaluation_.Summary.AcceptedFindings,
            falsePositiveCount = evaluation_.Summary.FalsePositiveFindings,
            reviewRequiredCount = evaluation_.Summary.ReviewRequiredFindings,
            expiredBaselineEntryCount = evaluation_.Summary.ExpiredBaselineEntries,
            baselinePath = evaluation_.BaselinePath,
            baselinePresent = evaluation_.Baseline is not null,
            baselineAction = baselineAction_,
            summary = evaluation_.Summary,
            baseline = includeBaselineSummary_ ? evaluation_.Baseline : null,
            findings = evaluation_.Findings
        }, JsonOptions);
    }

    private static string WriteMarkdown(string repoRoot_, AuditEvaluationResult evaluation_, bool verbose_, bool includeBaselineSummary_, string? baselineAction_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Audit");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{repoRoot_}`");
        builder.AppendLine($"- Findings: `{evaluation_.Summary.Findings}`");
        builder.AppendLine($"- Active high severity: `{evaluation_.Summary.ActiveHighSeverity}`");
        builder.AppendLine($"- Accepted findings: `{evaluation_.Summary.AcceptedFindings}`");
        builder.AppendLine($"- False positives: `{evaluation_.Summary.FalsePositiveFindings}`");
        builder.AppendLine($"- Review required: `{evaluation_.Summary.ReviewRequiredFindings}`");
        builder.AppendLine($"- Expired baseline entries: `{evaluation_.Summary.ExpiredBaselineEntries}`");
        builder.AppendLine();

        if (includeBaselineSummary_)
        {
            builder.AppendLine("## Baseline");
            builder.AppendLine();
            builder.AppendLine($"- File: `{evaluation_.BaselinePath}`");
            builder.AppendLine($"- Present: `{evaluation_.Baseline is not null}`");
            builder.AppendLine($"- Total entries: `{evaluation_.Summary.BaselineEntries}`");
            builder.AppendLine($"- Accepted: `{evaluation_.Summary.AcceptedEntries}`");
            builder.AppendLine($"- False positive: `{evaluation_.Summary.FalsePositiveEntries}`");
            builder.AppendLine($"- Review required: `{evaluation_.Summary.ReviewRequiredEntries}`");
            builder.AppendLine($"- Expired: `{evaluation_.Summary.ExpiredBaselineEntries}`");
            if (!string.IsNullOrWhiteSpace(baselineAction_))
            {
                builder.AppendLine($"- Action: {baselineAction_}");
            }

            builder.AppendLine();
        }

        builder.AppendLine("## Findings");
        builder.AppendLine();
        if (evaluation_.Findings.Count == 0)
        {
            builder.AppendLine("- None");
            return builder.ToString().TrimEnd();
        }

        IEnumerable<AuditFinding> selected = verbose_ ? evaluation_.Findings : evaluation_.Findings.Take(200);
        builder.AppendLine("| Severity | Category | BaselineStatus | File | Line | Preview |");
        builder.AppendLine("| --- | --- | --- | --- | ---: | --- |");
        foreach (AuditFinding finding in selected)
        {
            builder.AppendLine($"| {finding.Severity} | {finding.Category} | {FormatBaselineStatus(finding)} | `{finding.File}` | {finding.Line} | {finding.Preview.Replace("|", "\\|", StringComparison.Ordinal)} |");
        }

        if (!verbose_ && evaluation_.Findings.Count > 200)
        {
            builder.AppendLine();
            builder.AppendLine($"Showing first 200 findings. Use `--verbose` to show all findings.");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatBaselineStatus(AuditFinding finding_)
    {
        string status = finding_.BaselineStatus switch
        {
            AuditBaselineStatus.Accepted => "accepted",
            AuditBaselineStatus.FalsePositive => "false-positive",
            _ => "review-required"
        };

        return finding_.IsExpired ? $"{status} (expired)" : status;
    }
}
