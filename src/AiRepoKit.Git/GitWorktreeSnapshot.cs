namespace AiRepoKit.Git;

public sealed class GitWorktreeSnapshot
{
    internal GitWorktreeSnapshot(
        GitWorktreeRequest request_,
        string worktreePath_,
        GitWorktreeState state_,
        bool isRegistered_,
        bool isPathPresent_,
        string? observedCommitSha_,
        bool? isDetached_,
        bool? isClean_,
        bool isLocked_,
        bool isPrunable_)
    {
        Request =
            request_;

        WorktreePath =
            worktreePath_;

        State =
            state_;

        IsRegistered =
            isRegistered_;

        IsPathPresent =
            isPathPresent_;

        ObservedCommitSha =
            observedCommitSha_;

        IsDetached =
            isDetached_;

        IsClean =
            isClean_;

        IsLocked =
            isLocked_;

        IsPrunable =
            isPrunable_;
    }

    public GitWorktreeRequest Request { get; }

    public string WorktreePath { get; }

    public GitWorktreeState State { get; }

    public bool IsRegistered { get; }

    public bool IsPathPresent { get; }

    public string? ObservedCommitSha { get; }

    public bool? IsDetached { get; }

    public bool? IsClean { get; }

    public bool IsLocked { get; }

    public bool IsPrunable { get; }
}
