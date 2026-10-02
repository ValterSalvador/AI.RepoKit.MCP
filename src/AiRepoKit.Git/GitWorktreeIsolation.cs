namespace AiRepoKit.Git;

using System.Security.Cryptography;
using System.Text;

public sealed class GitWorktreeIsolation
{
    private readonly TimeSpan _commandTimeout;
    private readonly IGitProcessRunner _processRunner;

    public GitWorktreeIsolation(
        TimeSpan commandTimeout_)
        : this(
            commandTimeout_,
            new GitProcessRunner())
    {
    }

    internal GitWorktreeIsolation(
        TimeSpan commandTimeout_,
        IGitProcessRunner processRunner_)
    {
        if (commandTimeout_ <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(commandTimeout_));
        }

        ArgumentNullException.ThrowIfNull(
            processRunner_);

        _commandTimeout =
            commandTimeout_;

        _processRunner =
            processRunner_;
    }

    public Task<GitWorktreeSnapshot> InspectAsync(
        GitWorktreeRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_);

        return InspectCoreAsync(
            request_,
            cancellationToken_);
    }

    public async Task<GitWorktreeSnapshot> EnsureAsync(
        GitWorktreeRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_);

        GitWorktreeSnapshot snapshot =
            await InspectCoreAsync(
                request_,
                cancellationToken_);

        switch (snapshot.State)
        {
            case GitWorktreeState.Ready:
                return snapshot;

            case GitWorktreeState.Dirty:
                throw new InvalidOperationException(
                    "Dirty worktree cannot be converged automatically.");

            case GitWorktreeState.Conflict:
                throw new InvalidOperationException(
                    "Conflicting worktree state cannot be converged automatically.");

            case GitWorktreeState.Missing:
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported worktree state.");
        }

        Directory.CreateDirectory(
            request_.IsolationRoot);

        await RunCheckedAsync(
            request_.RepositoryRoot,
            [
                "worktree",
                "add",
                "--detach",
                snapshot.WorktreePath,
                request_.BaseCommitSha
            ],
            cancellationToken_);

        GitWorktreeSnapshot created =
            await InspectCoreAsync(
                request_,
                cancellationToken_);

        if (created.State != GitWorktreeState.Ready)
        {
            throw new InvalidOperationException(
                "Created worktree did not converge to Ready.");
        }

        return created;
    }

    public async Task<GitWorktreeSnapshot> ReleaseAsync(
        GitWorktreeRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_);

        GitWorktreeSnapshot snapshot =
            await InspectCoreAsync(
                request_,
                cancellationToken_);

        switch (snapshot.State)
        {
            case GitWorktreeState.Missing:
                return snapshot;

            case GitWorktreeState.Dirty:
                throw new InvalidOperationException(
                    "Dirty worktree cannot be removed automatically.");

            case GitWorktreeState.Conflict:
                throw new InvalidOperationException(
                    "Conflicting worktree state cannot be removed automatically.");

            case GitWorktreeState.Ready:
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported worktree state.");
        }

        await RunCheckedAsync(
            request_.RepositoryRoot,
            [
                "worktree",
                "remove",
                snapshot.WorktreePath
            ],
            cancellationToken_);

        GitWorktreeSnapshot released =
            await InspectCoreAsync(
                request_,
                cancellationToken_);

        if (released.State != GitWorktreeState.Missing)
        {
            throw new InvalidOperationException(
                "Released worktree did not converge to Missing.");
        }

        return released;
    }

    private async Task<GitWorktreeSnapshot> InspectCoreAsync(
        GitWorktreeRequest request_,
        CancellationToken cancellationToken_)
    {
        string worktreePath =
            ComputeWorktreePath(
                request_);

        ValidateWorktreePath(
            request_,
            worktreePath);

        await ValidateRepositoryAsync(
            request_,
            cancellationToken_);

        GitProcessResult listResult =
            await RunCheckedAsync(
                request_.RepositoryRoot,
                [
                    "worktree",
                    "list",
                    "--porcelain",
                    "-z"
                ],
                cancellationToken_);

        IReadOnlyList<GitWorktreePorcelainEntry> entries =
            GitWorktreePorcelainParser.Parse(
                listResult.StandardOutput);

        GitWorktreePorcelainEntry[] matching =
            entries
                .Where(
                    entry_ =>
                        PathsEqual(
                            NormalizeObservedPath(
                                entry_.WorktreePath),
                            worktreePath))
                .ToArray();

        if (matching.Length > 1)
        {
            throw new InvalidDataException(
                "Expected worktree path is registered more than once.");
        }

        bool isDirectory =
            Directory.Exists(
                worktreePath);

        bool isPathPresent =
            isDirectory ||
            File.Exists(
                worktreePath);

        if (matching.Length == 0)
        {
            GitWorktreeState missingState =
                isPathPresent
                    ? GitWorktreeState.Conflict
                    : GitWorktreeState.Missing;

            return new GitWorktreeSnapshot(
                request_,
                worktreePath,
                missingState,
                isRegistered_: false,
                isPathPresent_: isPathPresent,
                observedCommitSha_: null,
                isDetached_: null,
                isClean_: null,
                isLocked_: false,
                isPrunable_: false);
        }

        GitWorktreePorcelainEntry entry =
            matching[0];

        bool? isClean =
            null;

        if (isDirectory)
        {
            GitProcessResult statusResult =
                await RunCheckedAsync(
                    worktreePath,
                    [
                        "status",
                        "--porcelain=v1",
                        "-z",
                        "--untracked-files=all"
                    ],
                    cancellationToken_);

            isClean =
                statusResult.StandardOutput.Length == 0;
        }

        bool expectedCommit =
            string.Equals(
                entry.HeadSha,
                request_.BaseCommitSha,
                StringComparison.Ordinal);

        bool commonReadyRequirements =
            isDirectory &&
            expectedCommit &&
            entry.IsDetached &&
            !entry.IsLocked &&
            !entry.IsPrunable;

        GitWorktreeState state;

        if (
            commonReadyRequirements &&
            isClean == true)
        {
            state =
                GitWorktreeState.Ready;
        }
        else if (
            commonReadyRequirements &&
            isClean == false)
        {
            state =
                GitWorktreeState.Dirty;
        }
        else
        {
            state =
                GitWorktreeState.Conflict;
        }

        return new GitWorktreeSnapshot(
            request_,
            worktreePath,
            state,
            isRegistered_: true,
            isPathPresent_: isPathPresent,
            observedCommitSha_: entry.HeadSha,
            isDetached_: entry.IsDetached,
            isClean_: isClean,
            isLocked_: entry.IsLocked,
            isPrunable_: entry.IsPrunable);
    }

    private async Task ValidateRepositoryAsync(
        GitWorktreeRequest request_,
        CancellationToken cancellationToken_)
    {
        if (!Directory.Exists(
                request_.RepositoryRoot))
        {
            throw new DirectoryNotFoundException(
                "Repository root does not exist.");
        }

        GitProcessResult topLevelResult =
            await RunCheckedAsync(
                request_.RepositoryRoot,
                [
                    "rev-parse",
                    "--show-toplevel"
                ],
                cancellationToken_);

        string topLevel =
            ReadSingleLineOutput(
                topLevelResult.StandardOutput,
                "repository top-level");

        if (!Path.IsPathFullyQualified(
                topLevel))
        {
            throw new InvalidDataException(
                "Git repository top-level path is not fully qualified.");
        }

        string normalizedTopLevel =
            Path.GetFullPath(
                topLevel);

        if (!PathsEqual(
                normalizedTopLevel,
                request_.RepositoryRoot))
        {
            throw new InvalidOperationException(
                "RepositoryRoot must be the Git worktree top-level.");
        }

        GitProcessResult commitResult =
            await RunCheckedAsync(
                request_.RepositoryRoot,
                [
                    "rev-parse",
                    "--verify",
                    $"{request_.BaseCommitSha}^{{commit}}"
                ],
                cancellationToken_);

        string resolvedCommit =
            ReadSingleLineOutput(
                commitResult.StandardOutput,
                "resolved commit");

        if (!IsObjectId(
                resolvedCommit))
        {
            throw new InvalidDataException(
                "Git returned a malformed commit object ID.");
        }

        if (!string.Equals(
                resolvedCommit,
                request_.BaseCommitSha,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Base commit did not resolve to the exact requested object ID.");
        }
    }

    private async Task<GitProcessResult> RunCheckedAsync(
        string workingDirectory_,
        IReadOnlyList<string> arguments_,
        CancellationToken cancellationToken_)
    {
        GitProcessResult result =
            await _processRunner.RunAsync(
                workingDirectory_,
                arguments_,
                _commandTimeout,
                cancellationToken_);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git command failed with exit code {result.ExitCode}. {result.StandardError}");
        }

        return result;
    }

    private static string ComputeWorktreePath(
        GitWorktreeRequest request_)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(
                request_.IsolationId);

        byte[] hash =
            SHA256.HashData(
                bytes);

        string hashText =
            Convert
                .ToHexString(
                    hash)
                .ToLowerInvariant();

        return Path.GetFullPath(
            Path.Combine(
                request_.IsolationRoot,
                $"worktree-{hashText}"));
    }

    private static void ValidateWorktreePath(
        GitWorktreeRequest request_,
        string worktreePath_)
    {
        if (IsSameOrDescendant(
                worktreePath_,
                request_.RepositoryRoot))
        {
            throw new ArgumentException(
                "Derived worktree path must be strictly outside RepositoryRoot.",
                nameof(request_));
        }
    }

    private static string NormalizeObservedPath(
        string path_)
    {
        try
        {
            if (
                string.IsNullOrWhiteSpace(
                    path_) ||
                !Path.IsPathFullyQualified(
                    path_))
            {
                throw new InvalidDataException(
                    "Git returned a malformed worktree path.");
            }

            return Path.GetFullPath(
                path_);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception_)
            when (
                exception_ is ArgumentException ||
                exception_ is NotSupportedException ||
                exception_ is PathTooLongException)
        {
            throw new InvalidDataException(
                "Git returned a malformed worktree path.",
                exception_);
        }
    }

    private static string ReadSingleLineOutput(
        string output_,
        string description_)
    {
        string value =
            output_.TrimEnd(
                '\r',
                '\n');

        if (
            value.Length == 0 ||
            value.Contains(
                '\r') ||
            value.Contains(
                '\n'))
        {
            throw new InvalidDataException(
                $"Git returned malformed {description_} output.");
        }

        return value;
    }

    private static bool IsObjectId(
        string value_)
    {
        if (
            value_.Length != 40 &&
            value_.Length != 64)
        {
            return false;
        }

        foreach (char value in value_)
        {
            if (
                !(
                    value >= '0' &&
                    value <= '9'
                ) &&
                !(
                    value >= 'a' &&
                    value <= 'f'
                ))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PathsEqual(
        string left_,
        string right_)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    left_)),
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    right_)),
            PathComparison);
    }

    private static bool IsSameOrDescendant(
        string candidate_,
        string root_)
    {
        string candidate =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    candidate_));

        string root =
            Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(
                    root_));

        if (string.Equals(
                candidate,
                root,
                PathComparison))
        {
            return true;
        }

        string prefix =
            root;

        if (
            !prefix.EndsWith(
                Path.DirectorySeparatorChar) &&
            !prefix.EndsWith(
                Path.AltDirectorySeparatorChar))
        {
            prefix +=
                Path.DirectorySeparatorChar;
        }

        return candidate.StartsWith(
            prefix,
            PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
