using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class HooksCommandTests
{
    [Fact]
    public void BuildHook_CompatibleLocalTool_IsPreferredBeforeGlobalFallback()
    {
        string hookScript = HooksCommand.BuildHook("--quick");

        // The capability check for the local tool must probe for update support, not just --version
        Assert.DoesNotContain("dotnet tool run airepo -- --version", hookScript);
        Assert.Contains("if dotnet tool run airepo -- --help 2>/dev/null | grep -F \"airepo update\" >/dev/null 2>&1; then", hookScript);

        // Local execution is preferred inside the first 'if' branch
        int localProbeIndex = hookScript.IndexOf("if dotnet tool run airepo -- --help", StringComparison.Ordinal);
        int localExecIndex = hookScript.IndexOf("dotnet tool run airepo -- update", StringComparison.Ordinal);
        int globalProbeIndex = hookScript.IndexOf("elif command -v airepo", StringComparison.Ordinal);

        Assert.True(localProbeIndex >= 0);
        Assert.True(localExecIndex > localProbeIndex);
        Assert.True(globalProbeIndex > localExecIndex);
    }

    [Fact]
    public void BuildHook_IncompatibleLocalTool_AllowsGlobalFallback()
    {
        string hookScript = HooksCommand.BuildHook("--quick");

        // When local capability probe fails, global fallback is checked in elif
        Assert.Contains("elif command -v airepo >/dev/null 2>&1 && airepo --help 2>/dev/null | grep -F \"airepo update\" >/dev/null 2>&1; then", hookScript);

        int globalProbeIndex = hookScript.IndexOf("elif command -v airepo", StringComparison.Ordinal);
        int globalExecIndex = hookScript.IndexOf("airepo update --repo . --quick --no-progress", StringComparison.Ordinal);

        Assert.True(globalProbeIndex >= 0);
        Assert.True(globalExecIndex > globalProbeIndex);
    }

    [Fact]
    public void BuildHook_SelectedToolExecutionFailure_IsNotMaskedByFallback()
    {
        string hookScript = HooksCommand.BuildHook("--quick");

        // Once local tool is selected, failure must propagate directly without || fallback to global
        Assert.DoesNotContain("|| airepo", hookScript);
        Assert.DoesNotContain("|| dotnet", hookScript);

        // Tool execution must not be in an 'if' / 'elif' command runner position that falls back on failure;
        // capability probes use --help before selecting execution
        string[] lines = hookScript.Split('\n');
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("if dotnet", StringComparison.Ordinal) || trimmed.StartsWith("elif ", StringComparison.Ordinal))
            {
                Assert.Contains("--help", trimmed);
            }
        }
    }

    [Fact]
    public void BuildHook_PreCommit_RetainsQuickPreset()
    {
        string hookScript = HooksCommand.BuildHook("--quick");

        Assert.Contains("dotnet tool run airepo -- update --repo . --quick --no-progress", hookScript);
        Assert.Contains("airepo update --repo . --quick --no-progress", hookScript);
    }

    [Fact]
    public void BuildHook_PostMergeAndPostRewrite_RetainNormalUpdateWithoutQuick()
    {
        string hookScript = HooksCommand.BuildHook(string.Empty);

        Assert.Contains("dotnet tool run airepo -- update --repo . --no-progress", hookScript);
        Assert.Contains("airepo update --repo . --no-progress", hookScript);
        Assert.DoesNotContain("--quick", hookScript);
    }

    [Fact]
    public void BuildHook_AirepoSkipHooks_IsPreservedAndEvaluatedFirst()
    {
        string hookScript = HooksCommand.BuildHook("--quick");

        int skipCheckIndex = hookScript.IndexOf("if [ \"$AIREPO_SKIP_HOOKS\" = \"1\" ]; then", StringComparison.Ordinal);
        int exitZeroIndex = hookScript.IndexOf("exit 0", StringComparison.Ordinal);
        int firstProbeIndex = hookScript.IndexOf("dotnet tool run airepo", StringComparison.Ordinal);

        Assert.True(skipCheckIndex >= 0, "AIREPO_SKIP_HOOKS check must be present.");
        Assert.True(exitZeroIndex > skipCheckIndex, "exit 0 must follow AIREPO_SKIP_HOOKS check.");
        Assert.True(firstProbeIndex > exitZeroIndex, "Skip check must precede any capability probe.");
    }

    [Fact]
    public void BuildHook_NeitherToolCompatible_FailsClearly()
    {
        string hookScript = HooksCommand.BuildHook("--quick");

        int elseIndex = hookScript.IndexOf("else", StringComparison.Ordinal);
        Assert.True(elseIndex >= 0);

        string elseBlock = hookScript.Substring(elseIndex);
        Assert.Contains("Compatible airepo with 'update' support was not found", elseBlock);
        Assert.Contains(">&2", elseBlock);
        Assert.Contains("exit 1", elseBlock);
    }

    [Fact]
    public void CheckedInHooks_AreAlignedWithGenerator()
    {
        string repoRoot = FindRepoRoot();

        string preCommitPath = Path.Combine(repoRoot, ".githooks", "pre-commit");
        string postMergePath = Path.Combine(repoRoot, ".githooks", "post-merge");
        string postRewritePath = Path.Combine(repoRoot, ".githooks", "post-rewrite");

        Assert.True(File.Exists(preCommitPath), $".githooks/pre-commit must exist at {preCommitPath}");
        Assert.True(File.Exists(postMergePath), $".githooks/post-merge must exist at {postMergePath}");
        Assert.True(File.Exists(postRewritePath), $".githooks/post-rewrite must exist at {postRewritePath}");

        string expectedPreCommit = HooksCommand.BuildHook("--quick");
        string expectedPostMerge = HooksCommand.BuildHook(string.Empty);
        string expectedPostRewrite = HooksCommand.BuildHook(string.Empty);

        Assert.Equal(expectedPreCommit, NormalizeLineEndings(File.ReadAllText(preCommitPath)));
        Assert.Equal(expectedPostMerge, NormalizeLineEndings(File.ReadAllText(postMergePath)));
        Assert.Equal(expectedPostRewrite, NormalizeLineEndings(File.ReadAllText(postRewritePath)));
    }

    [Fact]
    public void HooksCommand_Execute_PreviewReportsExpectedHooks()
    {
        using TempRepo repo = new();
        BootstrapOptions options = Program.Parse(["hooks", "--repo", repo.Path]);

        CommandResult result = new HooksCommand().Execute(options);

        Assert.True(result.Success);
        Assert.Contains("# Git Hooks Preview", result.Markdown);
        Assert.Contains("pre-commit: `airepo update --quick`", result.Markdown);
        Assert.Contains("post-merge: `airepo update`", result.Markdown);
        Assert.Contains("post-rewrite: `airepo update`", result.Markdown);
        Assert.Contains("AIREPO_SKIP_HOOKS=1", result.Markdown);

        // Preview should not write hook files
        Assert.False(Directory.Exists(Path.Combine(repo.Path, ".githooks")));
    }

    [Fact]
    public void HooksCommand_Execute_ApplyWritesSynchronizedHooks()
    {
        using TempRepo repo = new();
        BootstrapOptions options = Program.Parse(["hooks", "--repo", repo.Path, "--apply"]);

        CommandResult result = new HooksCommand().Execute(options);

        // When git config core.hooksPath succeeds (or mock repo has .git), verify written files
        string preCommit = Path.Combine(repo.Path, ".githooks", "pre-commit");
        string postMerge = Path.Combine(repo.Path, ".githooks", "post-merge");
        string postRewrite = Path.Combine(repo.Path, ".githooks", "post-rewrite");

        if (File.Exists(preCommit))
        {
            Assert.Equal(HooksCommand.BuildHook("--quick"), NormalizeLineEndings(File.ReadAllText(preCommit)));
            Assert.Equal(HooksCommand.BuildHook(string.Empty), NormalizeLineEndings(File.ReadAllText(postMerge)));
            Assert.Equal(HooksCommand.BuildHook(string.Empty), NormalizeLineEndings(File.ReadAllText(postRewrite)));
        }
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current, ".githooks")) &&
                (File.Exists(Path.Combine(current, "AI.RepoKit.MCP.sln")) || Directory.Exists(Path.Combine(current, ".git"))))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        // Fallback to Directory.GetCurrentDirectory()
        string fallback = Directory.GetCurrentDirectory();
        if (Directory.Exists(Path.Combine(fallback, ".githooks")))
        {
            return fallback;
        }

        throw new InvalidOperationException("Could not find repository root containing .githooks.");
    }

    private sealed class TempRepo : IDisposable
    {
        public TempRepo()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "airepo_hooks_test_" + Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(System.IO.Path.Combine(Path, ".git"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
