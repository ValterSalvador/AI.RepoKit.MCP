namespace AiRepoKit.Git;

public sealed class GitWorktreeRequest
{
    public GitWorktreeRequest(
        string repositoryRoot_,
        string isolationRoot_,
        string isolationId_,
        string baseCommitSha_)
    {
        RepositoryRoot =
            ValidatePath(
                repositoryRoot_,
                nameof(repositoryRoot_));

        IsolationRoot =
            ValidatePath(
                isolationRoot_,
                nameof(isolationRoot_));

        IsolationId =
            ValidateIsolationId(
                isolationId_);

        BaseCommitSha =
            ValidateBaseCommitSha(
                baseCommitSha_);
    }

    public string RepositoryRoot { get; }

    public string IsolationRoot { get; }

    public string IsolationId { get; }

    public string BaseCommitSha { get; }

    private static string ValidatePath(
        string value_,
        string parameterName_)
    {
        ArgumentNullException.ThrowIfNull(
            value_,
            parameterName_);

        if (
            string.IsNullOrWhiteSpace(
                value_) ||
            !string.Equals(
                value_,
                value_.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Path must be non-blank and must not contain outer whitespace.",
                parameterName_);
        }

        if (!Path.IsPathFullyQualified(
                value_))
        {
            throw new ArgumentException(
                "Path must be fully qualified.",
                parameterName_);
        }

        return Path.GetFullPath(
            value_);
    }

    private static string ValidateIsolationId(
        string value_)
    {
        ArgumentNullException.ThrowIfNull(
            value_);

        if (
            string.IsNullOrWhiteSpace(
                value_) ||
            !string.Equals(
                value_,
                value_.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Isolation ID must be non-blank and must not contain outer whitespace.",
                nameof(value_));
        }

        if (
            value_.Length > 128 ||
            !IsAsciiAlphaNumeric(
                value_[0]))
        {
            throw new ArgumentException(
                "Isolation ID is not canonical.",
                nameof(value_));
        }

        for (
            int index = 1;
            index < value_.Length;
            index++)
        {
            char value =
                value_[index];

            if (
                !IsAsciiAlphaNumeric(
                    value) &&
                value != '.' &&
                value != '_' &&
                value != ':' &&
                value != '-')
            {
                throw new ArgumentException(
                    "Isolation ID is not canonical.",
                    nameof(value_));
            }
        }

        return value_;
    }

    private static string ValidateBaseCommitSha(
        string value_)
    {
        ArgumentNullException.ThrowIfNull(
            value_);

        if (
            string.IsNullOrWhiteSpace(
                value_) ||
            !string.Equals(
                value_,
                value_.Trim(),
                StringComparison.Ordinal) ||
            (
                value_.Length != 40 &&
                value_.Length != 64
            ))
        {
            throw new ArgumentException(
                "Base commit SHA must be a canonical full lowercase object ID.",
                nameof(value_));
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
                throw new ArgumentException(
                    "Base commit SHA must be a canonical full lowercase object ID.",
                    nameof(value_));
            }
        }

        return value_;
    }

    private static bool IsAsciiAlphaNumeric(
        char value_)
    {
        return
            (
                value_ >= 'a' &&
                value_ <= 'z'
            ) ||
            (
                value_ >= 'A' &&
                value_ <= 'Z'
            ) ||
            (
                value_ >= '0' &&
                value_ <= '9'
            );
    }
}
