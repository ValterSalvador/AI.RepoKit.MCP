using System.Globalization;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Commands.Spec;

public sealed record SpecInitOptions(
    SpecId SpecId,
    string FromPath,
    string? RepoPath,
    SpecWriteMode Mode,
    bool IsJson);

public sealed record SpecShowOptions(
    SpecId SpecId,
    string? RepoPath,
    string ArtifactSelector,
    bool IsJson);

public sealed record SpecRefineOptions(
    SpecId SpecId,
    string Artifact,
    string FromPath,
    ArtifactRevision? ExpectedRevision,
    string? RepoPath,
    SpecWriteMode Mode,
    bool IsJson);

public sealed record SpecApproveOptions(
    SpecId SpecId,
    string Artifact,
    ArtifactRevision Revision,
    string? RepoPath,
    SpecWriteMode Mode,
    bool IsJson);

public sealed class SpecCliParsingException : Exception
{
    public bool IsJson { get; }

    public SpecCliParsingException(string message, bool isJson) : base(message)
    {
        this.IsJson = isJson;
    }
}

public static class SpecCommandParser
{
    public static SpecInitOptions ParseInit(IReadOnlyList<string> args)
    {
        RawOptions options = ParseRawOptions(
            "init",
            args,
            allowedValuedOptions: ["--spec-id", "--from", "--repo"],
            allowedFlagOptions: ["--dry-run", "--apply", "--json"]);

        SpecId specId = ParseSpecId(options);

        if (!options.Valued.TryGetValue("--from", out string? fromPath) || string.IsNullOrWhiteSpace(fromPath))
        {
            throw new SpecCliParsingException("Missing required option: '--from'.", options.IsJson);
        }

        SpecWriteMode mode = options.Apply ? SpecWriteMode.Apply : SpecWriteMode.DryRun;
        string? repoPath = options.Valued.GetValueOrDefault("--repo");

        return new SpecInitOptions(specId, fromPath, repoPath, mode, options.IsJson);
    }

    public static SpecShowOptions ParseShow(IReadOnlyList<string> args)
    {
        RawOptions options = ParseRawOptions(
            "show",
            args,
            allowedValuedOptions: ["--spec-id", "--repo", "--artifact"],
            allowedFlagOptions: ["--json"]);

        SpecId specId = ParseSpecId(options);

        string artifactSelector = options.Valued.GetValueOrDefault("--artifact", "all").ToLowerInvariant();
        if (artifactSelector is not ("requirements" or "work-spec" or "approvals" or "all"))
        {
            throw new SpecCliParsingException(
                $"Invalid or unsupported artifact selector '{artifactSelector}'. Allowed values: requirements, work-spec, approvals, all.",
                options.IsJson);
        }

        string? repoPath = options.Valued.GetValueOrDefault("--repo");
        return new SpecShowOptions(specId, repoPath, artifactSelector, options.IsJson);
    }

    public static SpecRefineOptions ParseRefine(IReadOnlyList<string> args)
    {
        RawOptions options = ParseRawOptions(
            "refine",
            args,
            allowedValuedOptions: ["--spec-id", "--artifact", "--from", "--expected-revision", "--repo"],
            allowedFlagOptions: ["--dry-run", "--apply", "--json"]);

        SpecId specId = ParseSpecId(options);

        if (!options.Valued.TryGetValue("--artifact", out string? artifactRaw) || string.IsNullOrWhiteSpace(artifactRaw))
        {
            throw new SpecCliParsingException("Missing required option: '--artifact'.", options.IsJson);
        }

        string artifactLower = artifactRaw.ToLowerInvariant();
        if (artifactLower is "implementation-plan" or "plan")
        {
            throw new SpecCliParsingException("Plan refinement is not supported.", options.IsJson);
        }

        if (artifactLower is not ("requirements" or "work-spec"))
        {
            throw new SpecCliParsingException(
                $"Invalid or unsupported artifact '{artifactRaw}'. Allowed values: requirements, work-spec.",
                options.IsJson);
        }

        if (!options.Valued.TryGetValue("--from", out string? fromPath) || string.IsNullOrWhiteSpace(fromPath))
        {
            throw new SpecCliParsingException("Missing required option: '--from'.", options.IsJson);
        }

        ArtifactRevision? expectedRevision = null;
        if (options.Valued.TryGetValue("--expected-revision", out string? expectedRevisionRaw))
        {
            if (!int.TryParse(expectedRevisionRaw, NumberStyles.None, CultureInfo.InvariantCulture, out int rev) || rev < 1)
            {
                throw new SpecCliParsingException("Expected revision must be a positive integer.", options.IsJson);
            }

            expectedRevision = new ArtifactRevision(rev);
        }

        SpecWriteMode mode = options.Apply ? SpecWriteMode.Apply : SpecWriteMode.DryRun;
        string? repoPath = options.Valued.GetValueOrDefault("--repo");

        return new SpecRefineOptions(specId, artifactLower, fromPath, expectedRevision, repoPath, mode, options.IsJson);
    }

    public static SpecApproveOptions ParseApprove(IReadOnlyList<string> args)
    {
        RawOptions options = ParseRawOptions(
            "approve",
            args,
            allowedValuedOptions: ["--spec-id", "--artifact", "--revision", "--repo"],
            allowedFlagOptions: ["--dry-run", "--apply", "--json"]);

        SpecId specId = ParseSpecId(options);

        if (!options.Valued.TryGetValue("--artifact", out string? artifactRaw) || string.IsNullOrWhiteSpace(artifactRaw))
        {
            throw new SpecCliParsingException("Missing required option: '--artifact'.", options.IsJson);
        }

        string artifactLower = artifactRaw.ToLowerInvariant();
        if (artifactLower is "implementation-plan" or "plan")
        {
            throw new SpecCliParsingException("Plan approval is not supported.", options.IsJson);
        }

        if (artifactLower is not ("requirements" or "work-spec"))
        {
            throw new SpecCliParsingException(
                $"Invalid or unsupported artifact '{artifactRaw}'. Allowed values: requirements, work-spec.",
                options.IsJson);
        }

        if (!options.Valued.TryGetValue("--revision", out string? revisionRaw) || string.IsNullOrWhiteSpace(revisionRaw))
        {
            throw new SpecCliParsingException("Missing required option: '--revision'.", options.IsJson);
        }

        if (!int.TryParse(revisionRaw, NumberStyles.None, CultureInfo.InvariantCulture, out int rev) || rev < 1)
        {
            throw new SpecCliParsingException("Revision must be a positive integer.", options.IsJson);
        }

        ArtifactRevision requestedRevision = new(rev);
        SpecWriteMode mode = options.Apply ? SpecWriteMode.Apply : SpecWriteMode.DryRun;
        string? repoPath = options.Valued.GetValueOrDefault("--repo");

        return new SpecApproveOptions(specId, artifactLower, requestedRevision, repoPath, mode, options.IsJson);
    }

    private static SpecId ParseSpecId(RawOptions options)
    {
        if (!options.Valued.TryGetValue("--spec-id", out string? specIdRaw) || string.IsNullOrWhiteSpace(specIdRaw))
        {
            throw new SpecCliParsingException("Missing required option: '--spec-id'.", options.IsJson);
        }

        if (!SpecId.TryParse(specIdRaw, out SpecId specId))
        {
            throw new SpecCliParsingException(
                $"Invalid Spec ID '{specIdRaw}'. Spec ID must be 1 through 64 lowercase ASCII letters, digits, or internal hyphens and must not be a Windows device name.",
                options.IsJson);
        }

        return specId;
    }

    private static RawOptions ParseRawOptions(
        string subcommand,
        IReadOnlyList<string> args,
        HashSet<string> allowedValuedOptions,
        HashSet<string> allowedFlagOptions)
    {
        bool isJson = args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> valued = new(StringComparer.OrdinalIgnoreCase);
        bool dryRun = false;
        bool apply = false;
        bool json = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!arg.StartsWith('-'))
            {
                throw new SpecCliParsingException($"Unexpected argument '{arg}' for 'spec {subcommand}'.", isJson);
            }

            if (!seen.Add(arg))
            {
                throw new SpecCliParsingException($"Duplicate option '{arg}'.", isJson);
            }

            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowedFlagOptions.Contains(arg))
                {
                    throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand}'.", isJson);
                }

                dryRun = true;
            }
            else if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowedFlagOptions.Contains(arg))
                {
                    throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand}'.", isJson);
                }

                apply = true;
            }
            else if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowedFlagOptions.Contains(arg))
                {
                    throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand}'.", isJson);
                }

                json = true;
            }
            else if (allowedValuedOptions.Contains(arg))
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new SpecCliParsingException($"Missing value for option '{arg}'.", isJson);
                }

                valued[arg] = args[++i];
            }
            else
            {
                throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand}'.", isJson);
            }
        }

        if (dryRun && apply)
        {
            throw new SpecCliParsingException("Cannot specify both '--dry-run' and '--apply'.", isJson);
        }

        return new RawOptions(valued, dryRun, apply, json);
    }

    private sealed record RawOptions(
        Dictionary<string, string> Valued,
        bool DryRun,
        bool Apply,
        bool IsJson);
}
