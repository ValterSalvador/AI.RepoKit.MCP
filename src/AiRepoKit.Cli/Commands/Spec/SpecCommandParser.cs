using System.Globalization;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.Commands.Spec;

public sealed record SpecInitOptions
{
    public SpecInitOptions(
        SpecId specId_,
        string fromPath_,
        string? repoPath_,
        SpecWriteMode mode_,
        bool isJson_)
    {
        this.SpecId = specId_;
        this.FromPath = fromPath_;
        this.RepoPath = repoPath_;
        this.Mode = mode_;
        this.IsJson = isJson_;
    }

    public SpecId SpecId { get; }

    public string FromPath { get; }

    public string? RepoPath { get; }

    public SpecWriteMode Mode { get; }

    public bool IsJson { get; }
}

public sealed record SpecShowOptions
{
    public SpecShowOptions(
        SpecId specId_,
        string? repoPath_,
        string artifactSelector_,
        bool isJson_)
    {
        this.SpecId = specId_;
        this.RepoPath = repoPath_;
        this.ArtifactSelector = artifactSelector_;
        this.IsJson = isJson_;
    }

    public SpecId SpecId { get; }

    public string? RepoPath { get; }

    public string ArtifactSelector { get; }

    public bool IsJson { get; }
}

public sealed record SpecRefineOptions
{
    public SpecRefineOptions(
        SpecId specId_,
        string artifact_,
        string fromPath_,
        ArtifactRevision? expectedRevision_,
        string? repoPath_,
        SpecWriteMode mode_,
        bool isJson_)
    {
        this.SpecId = specId_;
        this.Artifact = artifact_;
        this.FromPath = fromPath_;
        this.ExpectedRevision = expectedRevision_;
        this.RepoPath = repoPath_;
        this.Mode = mode_;
        this.IsJson = isJson_;
    }

    public SpecId SpecId { get; }

    public string Artifact { get; }

    public string FromPath { get; }

    public ArtifactRevision? ExpectedRevision { get; }

    public string? RepoPath { get; }

    public SpecWriteMode Mode { get; }

    public bool IsJson { get; }
}

public sealed record SpecApproveOptions
{
    public SpecApproveOptions(
        SpecId specId_,
        string artifact_,
        ArtifactRevision revision_,
        string? repoPath_,
        SpecWriteMode mode_,
        bool isJson_)
    {
        this.SpecId = specId_;
        this.Artifact = artifact_;
        this.Revision = revision_;
        this.RepoPath = repoPath_;
        this.Mode = mode_;
        this.IsJson = isJson_;
    }

    public SpecId SpecId { get; }

    public string Artifact { get; }

    public ArtifactRevision Revision { get; }

    public string? RepoPath { get; }

    public SpecWriteMode Mode { get; }

    public bool IsJson { get; }
}

public sealed class SpecCliParsingException : Exception
{
    public bool IsJson { get; }

    public SpecCliParsingException(string message_, bool isJson_) : base(message_)
    {
        this.IsJson = isJson_;
    }
}

public static class SpecCommandParser
{
    public static SpecInitOptions ParseInit(IReadOnlyList<string> args_)
    {
        RawOptions options = ParseRawOptions(
            "init",
            args_,
            allowedValuedOptions_: ["--spec-id", "--from", "--repo"],
            allowedFlagOptions_: ["--dry-run", "--apply", "--json"]);

        SpecId specId = ParseSpecId(options);

        if (!options.Valued.TryGetValue("--from", out string? fromPath) || string.IsNullOrWhiteSpace(fromPath))
        {
            throw new SpecCliParsingException("Missing required option: '--from'.", options.IsJson);
        }

        SpecWriteMode mode = options.Apply ? SpecWriteMode.Apply : SpecWriteMode.DryRun;
        string? repoPath = options.Valued.GetValueOrDefault("--repo");

        return new SpecInitOptions(specId, fromPath, repoPath, mode, options.IsJson);
    }

    public static SpecShowOptions ParseShow(IReadOnlyList<string> args_)
    {
        RawOptions options = ParseRawOptions(
            "show",
            args_,
            allowedValuedOptions_: ["--spec-id", "--repo", "--artifact"],
            allowedFlagOptions_: ["--json"]);

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

    public static SpecRefineOptions ParseRefine(IReadOnlyList<string> args_)
    {
        RawOptions options = ParseRawOptions(
            "refine",
            args_,
            allowedValuedOptions_: ["--spec-id", "--artifact", "--from", "--expected-revision", "--repo"],
            allowedFlagOptions_: ["--dry-run", "--apply", "--json"]);

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

    public static SpecApproveOptions ParseApprove(IReadOnlyList<string> args_)
    {
        RawOptions options = ParseRawOptions(
            "approve",
            args_,
            allowedValuedOptions_: ["--spec-id", "--artifact", "--revision", "--repo"],
            allowedFlagOptions_: ["--dry-run", "--apply", "--json"]);

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

    private static SpecId ParseSpecId(RawOptions options_)
    {
        if (!options_.Valued.TryGetValue("--spec-id", out string? specIdRaw) || string.IsNullOrWhiteSpace(specIdRaw))
        {
            throw new SpecCliParsingException("Missing required option: '--spec-id'.", options_.IsJson);
        }

        if (!SpecId.TryParse(specIdRaw, out SpecId specId))
        {
            throw new SpecCliParsingException(
                $"Invalid Spec ID '{specIdRaw}'. Spec ID must be 1 through 64 lowercase ASCII letters, digits, or internal hyphens and must not be a Windows device name.",
                options_.IsJson);
        }

        return specId;
    }

    private static RawOptions ParseRawOptions(
        string subcommand_,
        IReadOnlyList<string> args_,
        HashSet<string> allowedValuedOptions_,
        HashSet<string> allowedFlagOptions_)
    {
        bool isJson = args_.Any(arg_ => string.Equals(arg_, "--json", StringComparison.OrdinalIgnoreCase));
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> valued = new(StringComparer.OrdinalIgnoreCase);
        bool dryRun = false;
        bool apply = false;
        bool json = false;

        for (int i = 0; i < args_.Count; i++)
        {
            string arg = args_[i];
            if (!arg.StartsWith('-'))
            {
                throw new SpecCliParsingException($"Unexpected argument '{arg}' for 'spec {subcommand_}'.", isJson);
            }

            if (!seen.Add(arg))
            {
                throw new SpecCliParsingException($"Duplicate option '{arg}'.", isJson);
            }

            if (string.Equals(arg, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowedFlagOptions_.Contains(arg))
                {
                    throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand_}'.", isJson);
                }

                dryRun = true;
            }
            else if (string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowedFlagOptions_.Contains(arg))
                {
                    throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand_}'.", isJson);
                }

                apply = true;
            }
            else if (string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowedFlagOptions_.Contains(arg))
                {
                    throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand_}'.", isJson);
                }

                json = true;
            }
            else if (allowedValuedOptions_.Contains(arg))
            {
                if (i + 1 >= args_.Count || args_[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new SpecCliParsingException($"Missing value for option '{arg}'.", isJson);
                }

                valued[arg] = args_[++i];
            }
            else
            {
                throw new SpecCliParsingException($"Unknown option '{arg}' for 'spec {subcommand_}'.", isJson);
            }
        }

        if (dryRun && apply)
        {
            throw new SpecCliParsingException("Cannot specify both '--dry-run' and '--apply'.", isJson);
        }

        return new RawOptions(valued, dryRun, apply, json);
    }

    private sealed record RawOptions
    {
        public RawOptions(
            Dictionary<string, string> valued_,
            bool dryRun_,
            bool apply_,
            bool isJson_)
        {
            this.Valued = valued_;
            this.DryRun = dryRun_;
            this.Apply = apply_;
            this.IsJson = isJson_;
        }

        public Dictionary<string, string> Valued { get; }

        public bool DryRun { get; }

        public bool Apply { get; }

        public bool IsJson { get; }
    }
}
