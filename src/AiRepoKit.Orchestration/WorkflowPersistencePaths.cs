namespace AiRepoKit.Orchestration;

using System.Globalization;
using System.Text;

internal sealed class WorkflowPersistencePaths
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public string StorageRoot
    {
        get;
    }

    public WorkflowId WorkflowId
    {
        get;
    }

    public string RecordsDirectory
    {
        get;
    }

    public WorkflowPersistencePaths(
        string storageRoot_,
        WorkflowId workflowId_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            storageRoot_,
            nameof(storageRoot_));
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));

        string storageRoot =
            Path.GetFullPath(
                storageRoot_);

        if (!Directory.Exists(
                storageRoot))
        {
            throw new DirectoryNotFoundException(
                $"Workflow persistence storage root does not exist: '{storageRoot}'.");
        }

        string encodedWorkflowId =
            Convert
                .ToHexString(
                    _strictUtf8.GetBytes(
                        workflowId_.Value))
                .ToLowerInvariant();

        this.StorageRoot =
            storageRoot;
        this.WorkflowId =
            workflowId_;
        this.RecordsDirectory =
            Path.Combine(
                storageRoot,
                "workflows",
                encodedWorkflowId,
                "records");
    }

    public void EnsureRecordsDirectory()
    {
        if (!Directory.Exists(
                this.StorageRoot))
        {
            throw new DirectoryNotFoundException(
                $"Workflow persistence storage root does not exist: '{this.StorageRoot}'.");
        }

        Directory.CreateDirectory(
            this.RecordsDirectory);
    }

    public string GetRecordPath(
        long revision_)
    {
        if (revision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision_),
                revision_,
                "Persistence revision must be greater than zero.");
        }

        return Path.Combine(
            this.RecordsDirectory,
            FormatRecordFileName(
                revision_));
    }

    public static string FormatRecordFileName(
        long revision_)
    {
        if (revision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(revision_),
                revision_,
                "Persistence revision must be greater than zero.");
        }

        return string.Concat(
            revision_.ToString(
                "D20",
                CultureInfo.InvariantCulture),
            ".json");
    }

    public static bool TryParseRecordFileName(
        string fileName_,
        out long revision_)
    {
        revision_ =
            0;

        if (string.IsNullOrEmpty(
                fileName_) ||
            fileName_.Length != 25 ||
            !fileName_.EndsWith(
                ".json",
                StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> digits =
            fileName_.AsSpan(
                0,
                20);

        for (int index = 0; index < digits.Length; index++)
        {
            if (digits[index] < '0' ||
                digits[index] > '9')
            {
                return false;
            }
        }

        if (!long.TryParse(
                digits,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out long parsed) ||
            parsed <= 0)
        {
            return false;
        }

        if (!string.Equals(
                fileName_,
                FormatRecordFileName(
                    parsed),
                StringComparison.Ordinal))
        {
            return false;
        }

        revision_ =
            parsed;

        return true;
    }
}
