using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;

namespace AiRepoKit.Cli.Commands;

public sealed class HooksCommand
{
    private const string HooksPath = ".githooks";

    public CommandResult Execute(BootstrapOptions options_)
    {
        string repoRoot = Path.GetFullPath(options_.RepoPath);
        string gitDirectory = Path.Combine(repoRoot, ".git");
        if (!Directory.Exists(gitDirectory) && !File.Exists(gitDirectory))
        {
            return CommandResult.Failure("# Git Hooks\n\nThe target is not a Git repository.", 1);
        }

        bool apply = options_.Apply && !options_.DryRun;
        ProcessRunner runner = new();
        ProcessResult current = runner.Run("git", ["config", "--local", "--get", "core.hooksPath"], repoRoot);
        string currentPath = current.ExitCode == 0 ? current.StandardOutput.Trim() : string.Empty;

        if (!string.IsNullOrWhiteSpace(currentPath)
            && !string.Equals(Normalize(currentPath), HooksPath, StringComparison.OrdinalIgnoreCase)
            && !options_.Force)
        {
            return CommandResult.Failure(
                $"# Git Hooks\n\nExisting core.hooksPath is `{ProcessRunner.Redact(currentPath)}`. Use `--force` to replace it with `{HooksPath}`.",
                1);
        }

        IReadOnlyDictionary<string, string> hooks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pre-commit"] = BuildHook("--quick"),
            ["post-merge"] = BuildHook(string.Empty),
            ["post-rewrite"] = BuildHook(string.Empty)
        };

        List<string> conflicts = [];
        foreach ((string name, string script) in hooks)
        {
            string path = Path.Combine(repoRoot, HooksPath, name);
            if (File.Exists(path)
                && !string.Equals(File.ReadAllText(path), script, StringComparison.Ordinal)
                && !options_.Force)
            {
                conflicts.Add(name);
            }
        }

        if (conflicts.Count > 0)
        {
            return CommandResult.Failure(
                $"# Git Hooks\n\nRefusing to replace customized hook(s): `{string.Join("`, `", conflicts)}`. Use `--force` to replace them.",
                1);
        }

        if (apply)
        {
            Directory.CreateDirectory(Path.Combine(repoRoot, HooksPath));
            foreach ((string name, string script) in hooks)
            {
                string path = Path.Combine(repoRoot, HooksPath, name);
                File.WriteAllText(path, script, new UTF8Encoding(false));
                MakeExecutable(path);
            }

            ProcessResult configured = runner.Run("git", ["config", "--local", "core.hooksPath", HooksPath], repoRoot);
            if (configured.ExitCode != 0)
            {
                return CommandResult.Failure(
                    "# Git Hooks\n\nHook files were written, but git config failed: " + ProcessRunner.Redact(configured.StandardError),
                    1);
            }
        }

        StringBuilder builder = new();
        builder.AppendLine(apply ? "# Git Hooks Installed" : "# Git Hooks Preview");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{repoRoot}`");
        builder.AppendLine($"- core.hooksPath: `{HooksPath}`");
        builder.AppendLine("- pre-commit: `airepo update --quick`");
        builder.AppendLine("- post-merge: `airepo update`");
        builder.AppendLine("- post-rewrite: `airepo update`");
        builder.AppendLine("- escape hatch: set `AIREPO_SKIP_HOOKS=1` for one Git operation");
        if (!apply)
        {
            builder.AppendLine();
            builder.AppendLine("Run `airepo hooks --apply` to install the hooks.");
        }

        return CommandResult.Ok(builder.ToString().TrimEnd());
    }

    private static string BuildHook(string preset_)
    {
        string preset = string.IsNullOrWhiteSpace(preset_) ? string.Empty : " " + preset_;
        return string.Join('\n',
        [
            "#!/bin/sh",
            string.Empty,
            "if [ \"$AIREPO_SKIP_HOOKS\" = \"1\" ]; then",
            "  exit 0",
            "fi",
            string.Empty,
            "if dotnet tool run airepo -- --version >/dev/null 2>&1; then",
            $"  dotnet tool run airepo -- update --repo .{preset} --no-progress",
            "elif command -v airepo >/dev/null 2>&1; then",
            $"  airepo update --repo .{preset} --no-progress",
            "else",
            "  echo \"airepo was not found. Restore the local dotnet tool or set AIREPO_SKIP_HOOKS=1.\" >&2",
            "  exit 1",
            "fi",
            string.Empty
        ]);
    }

    private static string Normalize(string value_)
    {
        return value_.Replace('\\', '/').TrimEnd('/');
    }

    private static void MakeExecutable(string path_)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        UnixFileMode mode = File.GetUnixFileMode(path_);
        File.SetUnixFileMode(path_, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
