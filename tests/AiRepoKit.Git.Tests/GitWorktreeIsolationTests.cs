namespace AiRepoKit.Git.Tests;

using System.Security.Cryptography;
using System.Text;
using AiRepoKit.Git;
using Xunit;

public sealed class GitWorktreeIsolationTests
{
    private const string Sha =
        "0123456789abcdef0123456789abcdef01234567";

    private const string OtherSha =
        "89abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void Request_RejectsInvalidPathValues(
        int case_)
    {
        using Scenario scenario =
            new();

        string? repositoryRoot =
            scenario.RepositoryRoot;

        string? isolationRoot =
            scenario.IsolationRoot;

        switch (case_)
        {
            case 0:
                repositoryRoot = null;
                break;

            case 1:
                repositoryRoot = string.Empty;
                break;

            case 2:
                repositoryRoot = " ";
                break;

            case 3:
                repositoryRoot = "relative";
                break;

            case 4:
                repositoryRoot = " " + scenario.RepositoryRoot;
                break;

            case 5:
                isolationRoot = null;
                break;

            case 6:
                isolationRoot = string.Empty;
                break;

            case 7:
                isolationRoot = " ";
                break;

            case 8:
                isolationRoot = "relative";
                break;

            case 9:
                isolationRoot = scenario.IsolationRoot + " ";
                break;

            default:
                throw new InvalidOperationException();
        }

        Assert.ThrowsAny<ArgumentException>(
            () =>
                new GitWorktreeRequest(
                    repositoryRoot!,
                    isolationRoot!,
                    "task-1",
                    Sha));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" id")]
    [InlineData("id ")]
    [InlineData("-id")]
    [InlineData(".id")]
    [InlineData("_id")]
    [InlineData(":id")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a b")]
    [InlineData("a?")]
    [InlineData("ä")]
    public void Request_RejectsInvalidIsolationIds(
        string? isolationId_)
    {
        using Scenario scenario =
            new();

        Assert.ThrowsAny<ArgumentException>(
            () =>
                new GitWorktreeRequest(
                    scenario.RepositoryRoot,
                    scenario.IsolationRoot,
                    isolationId_!,
                    Sha));
    }

    [Fact]
    public void Request_RejectsIsolationIdLongerThan128()
    {
        using Scenario scenario =
            new();

        string isolationId =
            new(
                'a',
                129);

        Assert.Throws<ArgumentException>(
            () =>
                new GitWorktreeRequest(
                    scenario.RepositoryRoot,
                    scenario.IsolationRoot,
                    isolationId,
                    Sha));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("0")]
    [InlineData("a-b")]
    [InlineData("A_B")]
    [InlineData("a.b")]
    [InlineData("a:b")]
    [InlineData("a0._:-Z")]
    public void Request_AcceptsFrozenIsolationIdAlphabet(
        string isolationId_)
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            new(
                scenario.RepositoryRoot,
                scenario.IsolationRoot,
                isolationId_,
                Sha);

        Assert.Equal(
            isolationId_,
            request.IsolationId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0123456789ABCDEF0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("g123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef01234567 ")]
    [InlineData(" 0123456789abcdef0123456789abcdef01234567")]
    public void Request_RejectsInvalidBaseCommitSha(
        string? sha_)
    {
        using Scenario scenario =
            new();

        Assert.ThrowsAny<ArgumentException>(
            () =>
                new GitWorktreeRequest(
                    scenario.RepositoryRoot,
                    scenario.IsolationRoot,
                    "task-1",
                    sha_!));
    }

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void Request_AcceptsFullLowercaseObjectIds(
        string sha_)
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            new(
                scenario.RepositoryRoot,
                scenario.IsolationRoot,
                "task-1",
                sha_);

        Assert.Equal(
            sha_,
            request.BaseCommitSha);
    }

    [Fact]
    public void Request_NormalizesPathsAndPreservesLogicalIdentity()
    {
        using Scenario scenario =
            new();

        string repositoryInput =
            Path.Combine(
                scenario.RepositoryRoot,
                ".");

        string isolationInput =
            Path.Combine(
                scenario.IsolationRoot,
                ".");

        GitWorktreeRequest request =
            new(
                repositoryInput,
                isolationInput,
                "Task.A:1",
                Sha);

        Assert.Equal(
            Path.GetFullPath(
                repositoryInput),
            request.RepositoryRoot);

        Assert.Equal(
            Path.GetFullPath(
                isolationInput),
            request.IsolationRoot);

        Assert.Equal(
            "Task.A:1",
            request.IsolationId);

        Assert.Equal(
            Sha,
            request.BaseCommitSha);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Isolation_RejectsNonPositiveTimeout(
        long ticks_)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new GitWorktreeIsolation(
                    TimeSpan.FromTicks(
                        ticks_)));
    }

    [Fact]
    public async Task Inspect_RejectsNullRequest()
    {
        GitWorktreeIsolation isolation =
            new(
                TimeSpan.FromSeconds(
                    5));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () =>
                isolation.InspectAsync(
                    null!));
    }

    [Fact]
    public async Task Inspect_RejectsDerivedPathInsideRepository()
    {
        using Scenario scenario =
            new();

        string isolationRoot =
            Path.Combine(
                scenario.RepositoryRoot,
                "isolations");

        GitWorktreeRequest request =
            new(
                scenario.RepositoryRoot,
                isolationRoot,
                "task-1",
                Sha);

        FakeGitProcessRunner runner =
            new(
                request);

        GitWorktreeIsolation isolation =
            new(
                TimeSpan.FromSeconds(
                    5),
                runner);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                isolation.InspectAsync(
                    request));

        Assert.Empty(
            runner.Calls);
    }

    [Fact]
    public async Task Inspect_MissingStateIsObservable()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request);

        GitWorktreeIsolation isolation =
            scenario.CreateIsolation(
                runner);

        GitWorktreeSnapshot snapshot =
            await isolation.InspectAsync(
                request);

        Assert.Equal(
            GitWorktreeState.Missing,
            snapshot.State);

        Assert.False(
            snapshot.IsRegistered);

        Assert.False(
            snapshot.IsPathPresent);

        Assert.Null(
            snapshot.ObservedCommitSha);

        Assert.Null(
            snapshot.IsDetached);

        Assert.Null(
            snapshot.IsClean);

        Assert.False(
            snapshot.IsLocked);

        Assert.False(
            snapshot.IsPrunable);
    }

    [Fact]
    public async Task Inspect_UnregisteredExistingPathIsConflict()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            new(
                request);

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);

        Assert.False(
            snapshot.IsRegistered);

        Assert.True(
            snapshot.IsPathPresent);
    }

    [Fact]
    public async Task Inspect_RegisteredMissingPathIsConflict()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request)
            {
                Registered = true
            };

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);

        Assert.True(
            snapshot.IsRegistered);

        Assert.False(
            snapshot.IsPathPresent);

        Assert.Null(
            snapshot.IsClean);
    }

    [Fact]
    public async Task Inspect_ReadyStateRequiresExactDetachedCleanWorktree()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Ready,
            snapshot.State);

        Assert.True(
            snapshot.IsRegistered);

        Assert.True(
            snapshot.IsPathPresent);

        Assert.Equal(
            Sha,
            snapshot.ObservedCommitSha);

        Assert.True(
            snapshot.IsDetached);

        Assert.True(
            snapshot.IsClean);

        Assert.False(
            snapshot.IsLocked);

        Assert.False(
            snapshot.IsPrunable);
    }

    [Fact]
    public async Task Inspect_DirtyStateIncludesUntrackedChanges()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Dirty =
            true;

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Dirty,
            snapshot.State);

        Assert.False(
            snapshot.IsClean);
    }

    [Fact]
    public async Task Inspect_WrongHeadIsConflict()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.HeadSha =
            OtherSha;

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);
    }

    [Fact]
    public async Task Inspect_AttachedWorktreeIsConflict()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Detached =
            false;

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);

        Assert.False(
            snapshot.IsDetached);
    }

    [Fact]
    public async Task Inspect_LockedWorktreeIsConflict()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Locked =
            true;

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);

        Assert.True(
            snapshot.IsLocked);
    }

    [Fact]
    public async Task Inspect_PrunableWorktreeIsConflict()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Prunable =
            true;

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .InspectAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Conflict,
            snapshot.State);

        Assert.True(
            snapshot.IsPrunable);
    }

    [Fact]
    public async Task Inspect_DuplicateExpectedPathIsRejected()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        string entry =
            DetachedEntry(
                ComputeWorktreePath(
                    request),
                Sha);

        FakeGitProcessRunner runner =
            new(
                request)
            {
                ListOutputOverride =
                    entry +
                    entry
            };

        await Assert.ThrowsAsync<InvalidDataException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .InspectAsync(
                        request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("HEAD 0123456789abcdef0123456789abcdef01234567\0\0")]
    [InlineData("worktree /tmp/x\0\0")]
    [InlineData("worktree /tmp/x\0HEAD invalid\0detached\0\0")]
    [InlineData("worktree /tmp/x\0HEAD 0123456789abcdef0123456789abcdef01234567\0detached\0branch refs/heads/main\0\0")]
    public void Parser_RejectsMalformedPorcelain(
        string output_)
    {
        Assert.Throws<InvalidDataException>(
            () =>
                GitWorktreePorcelainParser.Parse(
                    output_));
    }

    [Fact]
    public void Parser_IgnoresUnknownFields()
    {
        string output =
            "worktree /tmp/x\0" +
            "HEAD 0123456789abcdef0123456789abcdef01234567\0" +
            "detached\0" +
            "future-field value\0" +
            "\0";

        GitWorktreePorcelainEntry entry =
            Assert.Single(
                GitWorktreePorcelainParser.Parse(
                    output));

        Assert.Equal(
            "/tmp/x",
            entry.WorktreePath);

        Assert.Equal(
            Sha,
            entry.HeadSha);

        Assert.True(
            entry.IsDetached);

        Assert.False(
            entry.IsLocked);

        Assert.False(
            entry.IsPrunable);
    }

    [Fact]
    public async Task Ensure_MissingCreatesDetachedWorktreeAndReturnsReady()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request);

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .EnsureAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Ready,
            snapshot.State);

        Assert.Equal(
            1,
            runner.AddCallCount);

        Assert.True(
            Directory.Exists(
                snapshot.WorktreePath));

        Assert.True(
            snapshot.IsDetached);

        Assert.True(
            snapshot.IsClean);
    }

    [Fact]
    public async Task Ensure_ReadyIsIdempotent()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .EnsureAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Ready,
            snapshot.State);

        Assert.Equal(
            0,
            runner.AddCallCount);

        Assert.Equal(
            0,
            runner.RemoveCallCount);
    }

    [Fact]
    public async Task Ensure_DirtyFailsClosedWithoutMutation()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Dirty =
            true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .EnsureAsync(
                        request));

        Assert.Equal(
            0,
            runner.AddCallCount);

        Assert.Equal(
            0,
            runner.RemoveCallCount);
    }

    [Fact]
    public async Task Ensure_ConflictFailsClosedWithoutMutation()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.HeadSha =
            OtherSha;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .EnsureAsync(
                        request));

        Assert.Equal(
            0,
            runner.AddCallCount);

        Assert.Equal(
            0,
            runner.RemoveCallCount);
    }

    [Fact]
    public async Task Release_MissingIsIdempotent()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request);

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .ReleaseAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Missing,
            snapshot.State);

        Assert.Equal(
            0,
            runner.RemoveCallCount);
    }

    [Fact]
    public async Task Release_ReadyRemovesAndReturnsMissing()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        string worktreePath =
            ComputeWorktreePath(
                request);

        Directory.CreateDirectory(
            worktreePath);

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        GitWorktreeSnapshot snapshot =
            await scenario
                .CreateIsolation(
                    runner)
                .ReleaseAsync(
                    request);

        Assert.Equal(
            GitWorktreeState.Missing,
            snapshot.State);

        Assert.Equal(
            1,
            runner.RemoveCallCount);

        Assert.False(
            Directory.Exists(
                worktreePath));
    }

    [Fact]
    public async Task Release_DirtyFailsClosedWithoutMutation()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Dirty =
            true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .ReleaseAsync(
                        request));

        Assert.Equal(
            0,
            runner.RemoveCallCount);
    }

    [Fact]
    public async Task Release_ConflictFailsClosedWithoutMutation()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        Directory.CreateDirectory(
            ComputeWorktreePath(
                request));

        FakeGitProcessRunner runner =
            ReadyRunner(
                request);

        runner.Locked =
            true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .ReleaseAsync(
                        request));

        Assert.Equal(
            0,
            runner.RemoveCallCount);
    }

    [Fact]
    public async Task NonZeroGitExitMapsToInvalidOperationException()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request)
            {
                TopLevelExitCode = 2
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .InspectAsync(
                        request));
    }

    [Fact]
    public async Task TimeoutFromProcessBoundaryIsPropagated()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request)
            {
                ExceptionToThrow =
                    new TimeoutException(
                        "simulated")
            };

        await Assert.ThrowsAsync<TimeoutException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .InspectAsync(
                        request));
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request);

        using CancellationTokenSource cancellation =
            new();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () =>
                scenario
                    .CreateIsolation(
                        runner)
                    .InspectAsync(
                        request,
                        cancellation.Token));
    }

    [Fact]
    public async Task FrozenTimeoutIsPassedToEveryGitInvocation()
    {
        using Scenario scenario =
            new();

        GitWorktreeRequest request =
            scenario.CreateRequest();

        FakeGitProcessRunner runner =
            new(
                request);

        TimeSpan timeout =
            TimeSpan.FromSeconds(
                17);

        GitWorktreeIsolation isolation =
            new(
                timeout,
                runner);

        await isolation.InspectAsync(
            request);

        Assert.NotEmpty(
            runner.Calls);

        Assert.All(
            runner.Calls,
            call_ =>
                Assert.Equal(
                    timeout,
                    call_.Timeout));
    }

    private static FakeGitProcessRunner ReadyRunner(
        GitWorktreeRequest request_)
    {
        return new FakeGitProcessRunner(
            request_)
        {
            Registered = true,
            HeadSha = request_.BaseCommitSha,
            Detached = true,
            Dirty = false,
            Locked = false,
            Prunable = false
        };
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

    private static string DetachedEntry(
        string path_,
        string headSha_,
        bool locked_ = false,
        bool prunable_ = false)
    {
        StringBuilder value =
            new();

        value.Append(
            "worktree ");
        value.Append(
            path_);
        value.Append('\0');

        value.Append(
            "HEAD ");
        value.Append(
            headSha_);
        value.Append('\0');

        value.Append(
            "detached");
        value.Append('\0');

        if (locked_)
        {
            value.Append(
                "locked test");
            value.Append('\0');
        }

        if (prunable_)
        {
            value.Append(
                "prunable test");
            value.Append('\0');
        }

        value.Append('\0');

        return value.ToString();
    }

    private sealed class Scenario : IDisposable
    {
        public Scenario()
        {
            Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "AiRepoKit.Git.Tests",
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
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string IsolationRoot { get; }

        public GitWorktreeRequest CreateRequest()
        {
            return new GitWorktreeRequest(
                RepositoryRoot,
                IsolationRoot,
                "task-1",
                Sha);
        }

        public GitWorktreeIsolation CreateIsolation(
            IGitProcessRunner runner_)
        {
            return new GitWorktreeIsolation(
                TimeSpan.FromSeconds(
                    5),
                runner_);
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    Root))
            {
                Directory.Delete(
                    Root,
                    recursive: true);
            }
        }
    }

    private sealed class FakeGitProcessRunner : IGitProcessRunner
    {
        private readonly GitWorktreeRequest _request;

        public FakeGitProcessRunner(
            GitWorktreeRequest request_)
        {
            _request =
                request_;

            HeadSha =
                request_.BaseCommitSha;
        }

        public bool Registered { get; set; }

        public string HeadSha { get; set; }

        public bool Detached { get; set; } =
            true;

        public bool Dirty { get; set; }

        public bool Locked { get; set; }

        public bool Prunable { get; set; }

        public int TopLevelExitCode { get; set; }

        public int VerifyExitCode { get; set; }

        public int AddExitCode { get; set; }

        public int RemoveExitCode { get; set; }

        public string? ListOutputOverride { get; set; }

        public Exception? ExceptionToThrow { get; set; }

        public int AddCallCount { get; private set; }

        public int RemoveCallCount { get; private set; }

        public List<Call> Calls { get; } =
            [];

        public Task<GitProcessResult> RunAsync(
            string workingDirectory_,
            IReadOnlyList<string> arguments_,
            TimeSpan timeout_,
            CancellationToken cancellationToken_)
        {
            cancellationToken_.ThrowIfCancellationRequested();

            if (ExceptionToThrow is not null)
            {
                return Task.FromException<GitProcessResult>(
                    ExceptionToThrow);
            }

            string[] arguments =
                arguments_.ToArray();

            Calls.Add(
                new Call(
                    workingDirectory_,
                    arguments,
                    timeout_));

            if (arguments.SequenceEqual(
                    new[]
                    {
                        "rev-parse",
                        "--show-toplevel"
                    }))
            {
                return Result(
                    TopLevelExitCode,
                    _request.RepositoryRoot +
                    Environment.NewLine);
            }

            if (
                arguments.Length == 3 &&
                arguments[0] == "rev-parse" &&
                arguments[1] == "--verify")
            {
                return Result(
                    VerifyExitCode,
                    _request.BaseCommitSha +
                    Environment.NewLine);
            }

            if (arguments.SequenceEqual(
                    new[]
                    {
                        "worktree",
                        "list",
                        "--porcelain",
                        "-z"
                    }))
            {
                return Result(
                    0,
                    ListOutputOverride ??
                    BuildWorktreeList());
            }

            if (arguments.SequenceEqual(
                    new[]
                    {
                        "status",
                        "--porcelain=v1",
                        "-z",
                        "--untracked-files=all"
                    }))
            {
                return Result(
                    0,
                    Dirty
                        ? "?? dirty.txt\0"
                        : string.Empty);
            }

            if (
                arguments.Length == 5 &&
                arguments[0] == "worktree" &&
                arguments[1] == "add" &&
                arguments[2] == "--detach")
            {
                AddCallCount++;

                if (AddExitCode != 0)
                {
                    return Result(
                        AddExitCode,
                        string.Empty,
                        "simulated add failure");
                }

                Registered =
                    true;

                HeadSha =
                    _request.BaseCommitSha;

                Detached =
                    true;

                Dirty =
                    false;

                Locked =
                    false;

                Prunable =
                    false;

                Directory.CreateDirectory(
                    arguments[3]);

                return Result(
                    0,
                    string.Empty);
            }

            if (
                arguments.Length == 3 &&
                arguments[0] == "worktree" &&
                arguments[1] == "remove")
            {
                RemoveCallCount++;

                if (RemoveExitCode != 0)
                {
                    return Result(
                        RemoveExitCode,
                        string.Empty,
                        "simulated remove failure");
                }

                Registered =
                    false;

                if (Directory.Exists(
                        arguments[2]))
                {
                    Directory.Delete(
                        arguments[2],
                        recursive: true);
                }

                return Result(
                    0,
                    string.Empty);
            }

            return Result(
                99,
                string.Empty,
                "unexpected fake git command");
        }

        private string BuildWorktreeList()
        {
            StringBuilder value =
                new();

            value.Append(
                "worktree ");
            value.Append(
                _request.RepositoryRoot);
            value.Append('\0');
            value.Append(
                "HEAD ");
            value.Append(
                _request.BaseCommitSha);
            value.Append('\0');
            value.Append(
                "branch refs/heads/main");
            value.Append('\0');
            value.Append('\0');

            if (Registered)
            {
                value.Append(
                    "worktree ");
                value.Append(
                    ComputeWorktreePath(
                        _request));
                value.Append('\0');

                value.Append(
                    "HEAD ");
                value.Append(
                    HeadSha);
                value.Append('\0');

                if (Detached)
                {
                    value.Append(
                        "detached");
                }
                else
                {
                    value.Append(
                        "branch refs/heads/test");
                }

                value.Append('\0');

                if (Locked)
                {
                    value.Append(
                        "locked test");
                    value.Append('\0');
                }

                if (Prunable)
                {
                    value.Append(
                        "prunable test");
                    value.Append('\0');
                }

                value.Append('\0');
            }

            return value.ToString();
        }

        private static Task<GitProcessResult> Result(
            int exitCode_,
            string standardOutput_,
            string standardError_ = "")
        {
            return Task.FromResult(
                new GitProcessResult(
                    exitCode_,
                    standardOutput_,
                    standardError_));
        }
    }

    private sealed record Call(
        string WorkingDirectory,
        string[] Arguments,
        TimeSpan Timeout);
}
