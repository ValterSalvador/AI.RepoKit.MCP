namespace AiRepoKit.Git;

internal sealed record GitWorktreePorcelainEntry(
    string WorktreePath,
    string HeadSha,
    bool IsDetached,
    bool IsLocked,
    bool IsPrunable);

internal static class GitWorktreePorcelainParser
{
    internal static IReadOnlyList<GitWorktreePorcelainEntry> Parse(
        string output_)
    {
        ArgumentNullException.ThrowIfNull(
            output_);

        if (
            output_.Length == 0 ||
            !output_.EndsWith(
                "\0\0",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Malformed git worktree porcelain output.");
        }

        string[] tokens =
            output_.Split(
                '\0');

        List<GitWorktreePorcelainEntry> entries =
            [];

        EntryBuilder? current =
            null;

        foreach (string token in tokens)
        {
            if (token.Length == 0)
            {
                if (current is not null)
                {
                    entries.Add(
                        current.Build());

                    current =
                        null;
                }

                continue;
            }

            if (token.StartsWith(
                    "worktree ",
                    StringComparison.Ordinal))
            {
                if (current is not null)
                {
                    throw new InvalidDataException(
                        "Worktree record separator is missing.");
                }

                string path =
                    token[
                        "worktree ".Length..
                    ];

                if (string.IsNullOrWhiteSpace(
                        path))
                {
                    throw new InvalidDataException(
                        "Worktree path is missing.");
                }

                current =
                    new EntryBuilder(
                        path);

                continue;
            }

            if (current is null)
            {
                throw new InvalidDataException(
                    "Worktree field appeared before a worktree path.");
            }

            current.Apply(
                token);
        }

        if (
            current is not null ||
            entries.Count == 0)
        {
            throw new InvalidDataException(
                "Malformed git worktree porcelain output.");
        }

        return entries;
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

    private sealed class EntryBuilder
    {
        private readonly string _worktreePath;

        private string? _headSha;
        private bool? _isDetached;
        private bool _isLocked;
        private bool _isPrunable;
        private bool _lockedSeen;
        private bool _prunableSeen;

        public EntryBuilder(
            string worktreePath_)
        {
            _worktreePath =
                worktreePath_;
        }

        public void Apply(
            string token_)
        {
            if (token_.StartsWith(
                    "HEAD ",
                    StringComparison.Ordinal))
            {
                if (_headSha is not null)
                {
                    throw new InvalidDataException(
                        "Duplicate HEAD field.");
                }

                string value =
                    token_[
                        "HEAD ".Length..
                    ];

                if (!IsObjectId(
                        value))
                {
                    throw new InvalidDataException(
                        "Malformed HEAD object ID.");
                }

                _headSha =
                    value;

                return;
            }

            if (token_ == "detached")
            {
                if (_isDetached.HasValue)
                {
                    throw new InvalidDataException(
                        "Duplicate worktree mode field.");
                }

                _isDetached =
                    true;

                return;
            }

            if (token_.StartsWith(
                    "branch ",
                    StringComparison.Ordinal))
            {
                if (_isDetached.HasValue)
                {
                    throw new InvalidDataException(
                        "Duplicate worktree mode field.");
                }

                string branch =
                    token_[
                        "branch ".Length..
                    ];

                if (string.IsNullOrWhiteSpace(
                        branch))
                {
                    throw new InvalidDataException(
                        "Branch field is malformed.");
                }

                _isDetached =
                    false;

                return;
            }

            if (
                token_ == "locked" ||
                token_.StartsWith(
                    "locked ",
                    StringComparison.Ordinal))
            {
                if (_lockedSeen)
                {
                    throw new InvalidDataException(
                        "Duplicate locked field.");
                }

                _lockedSeen =
                    true;

                _isLocked =
                    true;

                return;
            }

            if (
                token_ == "prunable" ||
                token_.StartsWith(
                    "prunable ",
                    StringComparison.Ordinal))
            {
                if (_prunableSeen)
                {
                    throw new InvalidDataException(
                        "Duplicate prunable field.");
                }

                _prunableSeen =
                    true;

                _isPrunable =
                    true;

                return;
            }

            // Unknown porcelain fields are intentionally ignored.
        }

        public GitWorktreePorcelainEntry Build()
        {
            if (
                _headSha is null ||
                !_isDetached.HasValue)
            {
                throw new InvalidDataException(
                    "Required worktree porcelain fields are missing.");
            }

            return new GitWorktreePorcelainEntry(
                _worktreePath,
                _headSha,
                _isDetached.Value,
                _isLocked,
                _isPrunable);
        }
    }
}
