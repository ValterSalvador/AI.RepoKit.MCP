namespace AiRepoKit.Git.Tests;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AiRepoKit.Git;
using Xunit;

public sealed class GitWorktreeIntegrationTests
{
    [Fact]
    public async Task LocalGit_EnsureInspectRelease_ConvergesDeterministically()
    {
        using GitScenario scenario =
            new();

        GitWorktreeIsolation isolation =
            new(
                TimeSpan.FromSeconds(
                    30));

        GitWorktreeRequest request =
            scenario.CreateRequest(
                "integration-create");

        GitWorktreeSnapshot ensured =
            await isolation.EnsureAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Ready,
            ensured.State);

        Assert.True(
            ensured.IsRegistered);

        Assert.True(
            ensured.IsPathPresent);

        Assert.True(
            ensured.IsDetached);

        Assert.True(
            ensured.IsClean);

        Assert.Equal(
            scenario.HeadSha,
            ensured.ObservedCommitSha);

        Assert.True(
            Directory.Exists(
                ensured.WorktreePath));

        GitWorktreeSnapshot inspected =
            await isolation.InspectAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Ready,
            inspected.State);

        Assert.Equal(
            ensured.WorktreePath,
            inspected.WorktreePath);

        GitWorktreeSnapshot released =
            await isolation.ReleaseAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Missing,
            released.State);

        Assert.False(
            released.IsRegistered);

        Assert.False(
            released.IsPathPresent);

        Assert.False(
            Directory.Exists(
                ensured.WorktreePath));
    }

    [Fact]
    public async Task LocalGit_EnsureReady_IsIdempotent()
    {
        using GitScenario scenario =
            new();

        GitWorktreeIsolation isolation =
            new(
                TimeSpan.FromSeconds(
                    30));

        GitWorktreeRequest request =
            scenario.CreateRequest(
                "integration-idempotent");

        GitWorktreeSnapshot first =
            await isolation.EnsureAsync(
                request);

        GitWorktreeSnapshot second =
            await isolation.EnsureAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Ready,
            first.State);

        Assert.Equal(
            GitWorktreeState.Ready,
            second.State);

        Assert.Equal(
            first.WorktreePath,
            second.WorktreePath);

        Assert.Equal(
            first.ObservedCommitSha,
            second.ObservedCommitSha);

        await isolation.ReleaseAsync(
            request);
    }

    [Fact]
    public async Task LocalGit_DirtyWorktree_IsDetectedAndReleaseFailsClosed()
    {
        using GitScenario scenario =
            new();

        GitWorktreeIsolation isolation =
            new(
                TimeSpan.FromSeconds(
                    30));

        GitWorktreeRequest request =
            scenario.CreateRequest(
                "integration-dirty");

        GitWorktreeSnapshot ready =
            await isolation.EnsureAsync(
                request);

        File.WriteAllText(
            Path.Combine(
                ready.WorktreePath,
                "untracked.txt"),
            "dirty");

        GitWorktreeSnapshot dirty =
            await isolation.InspectAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Dirty,
            dirty.State);

        Assert.False(
            dirty.IsClean);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                isolation.ReleaseAsync(
                    request));

        Assert.True(
            Directory.Exists(
                ready.WorktreePath));
    }

    [Fact]
    public async Task LocalGit_AttachedExpectedPath_IsConflict()
    {
        using GitScenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest(
                "integration-attached");

        string worktreePath =
            ComputeWorktreePath(
                request);

        Directory.CreateDirectory(
            request.IsolationRoot);

        RunGit(
            scenario.RepositoryRoot,
            "worktree",
            "add",
            "-b",
            "p05-test-branch",
            worktreePath,
            scenario.HeadSha);

        GitWorktreeIsolation isolation =
            new(
                TimeSpan.FromSeconds(
                    30));

        GitWorktreeSnapshot snapshot =
            await isolation.InspectAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);

        Assert.True(
            snapshot.IsRegistered);

        Assert.True(
            snapshot.IsPathPresent);

        Assert.False(
            snapshot.IsDetached);
    }

    private static string ComputeWorktreePath(
        GitWorktreeRequest request_)
    {
        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    request_.IsolationId));

        return Path.GetFullPath(
            Path.Combine(
                request_.IsolationRoot,
                "worktree-" +
                Convert
                    .ToHexString(
                        hash)
                    .ToLowerInvariant()));
    }

    private static string RunGit(
        string workingDirectory_,
        params string[] arguments_)
    {
        ProcessStartInfo startInfo =
            new()
            {
                FileName = "git",
                WorkingDirectory = workingDirectory_,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

        foreach (string argument in arguments_)
        {
            startInfo.ArgumentList.Add(
                argument);
        }

        using Process process =
            new()
            {
                StartInfo = startInfo
            };

        Assert.True(
            process.Start());

        string standardOutput =
            process.StandardOutput.ReadToEnd();

        string standardError =
            process.StandardError.ReadToEnd();

        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(" ", arguments_)} failed with exit code {process.ExitCode}: {standardError}");

        return standardOutput;
    }

    private sealed class GitScenario : IDisposable
    {
        public GitScenario()
        {
            Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "AiRepoKit.Git.Integration",
                    Guid
                        .NewGuid()
                        .ToString("N"));

            RepositoryRoot =
                Path.Combine(
                    Root,
                    "repository");

            IsolationRoot =
                Path.Combine(
                    Root,
                    "isolations");

            Directory.CreateDirectory(
                RepositoryRoot);

            RunGit(
                RepositoryRoot,
                "init");

            RunGit(
                RepositoryRoot,
                "config",
                "user.email",
                "ai-repokit-tests@example.invalid");

            RunGit(
                RepositoryRoot,
                "config",
                "user.name",
                "AI RepoKit Tests");

            RunGit(
                RepositoryRoot,
                "config",
                "commit.gpgSign",
                "false");

            RunGit(
                RepositoryRoot,
                "config",
                "core.hooksPath",
                ".git/no-hooks");

            File.WriteAllText(
                Path.Combine(
                    RepositoryRoot,
                    "seed.txt"),
                "seed");

            RunGit(
                RepositoryRoot,
                "add",
                "seed.txt");

            RunGit(
                RepositoryRoot,
                "commit",
                "-m",
                "initial");

            HeadSha =
                RunGit(
                    RepositoryRoot,
                    "rev-parse",
                    "HEAD")
                .Trim();
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string IsolationRoot { get; }

        public string HeadSha { get; }

        public GitWorktreeRequest CreateRequest(
            string isolationId_)
        {
            return new GitWorktreeRequest(
                RepositoryRoot,
                IsolationRoot,
                isolationId_,
                HeadSha);
        }

        public void Dispose()
        {
            if (!Directory.Exists(
                    Root))
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                foreach (
                    string path in
                    Directory.EnumerateFileSystemEntries(
                        Root,
                        "*",
                        SearchOption.AllDirectories))
                {
                    File.SetAttributes(
                        path,
                        FileAttributes.Normal);
                }

                File.SetAttributes(
                    Root,
                    FileAttributes.Normal);
            }

            Directory.Delete(
                Root,
                recursive: true);
        }
    }
}
