using System.Text;
using System.Text.Json;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class DetectCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public CommandResult Execute(BootstrapOptions options_)
    {
        RepoDetection detection = new RepoDetector().Detect(options_.RepoPath);
        if (options_.AuditJson)
        {
            return CommandResult.Ok(JsonSerializer.Serialize(new
            {
                repoRoot = detection.RepoRoot,
                recommendedProfile = detection.RecommendedProfile,
                confidence = detection.Confidence,
                detectedProfiles = detection.DetectedProfiles,
                signals = detection.Signals,
                evidence = detection.Evidence,
                warning = detection.Warning
            }, JsonOptions));
        }

        StringBuilder builder = new();
        builder.AppendLine("# Detect");
        builder.AppendLine();
        builder.AppendLine($"- RepoRoot: `{detection.RepoRoot}`");
        builder.AppendLine($"- RecommendedProfile: `{detection.RecommendedProfile}`");
        builder.AppendLine($"- Confidence: `{detection.Confidence:0.00}`");
        if (!string.IsNullOrWhiteSpace(detection.Warning))
        {
            builder.AppendLine($"- Warning: {detection.Warning}");
        }

        Append(builder, "Detected Profiles", detection.DetectedProfiles);
        Append(builder, "Signals", detection.Signals);
        Append(builder, "Evidence", detection.Evidence);
        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    private static void Append(StringBuilder builder_, string title_, IReadOnlyList<string> values_)
    {
        builder_.AppendLine();
        builder_.AppendLine($"## {title_}");
        builder_.AppendLine();
        if (values_.Count == 0)
        {
            builder_.AppendLine("- None");
            return;
        }

        foreach (string value in values_)
        {
            builder_.AppendLine($"- {ProcessRunner.Redact(value)}");
        }
    }
}
