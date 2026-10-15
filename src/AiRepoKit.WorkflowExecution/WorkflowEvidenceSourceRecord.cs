namespace AiRepoKit.WorkflowExecution;

public sealed record WorkflowEvidenceSourceRecord
{
    public string RelativePath
    {
        get;
    }

    public long ByteLength
    {
        get;
    }

    public string Sha256Fingerprint
    {
        get;
    }

    internal WorkflowEvidenceSourceRecord(
        string relativePath_,
        long byteLength_,
        string sha256Fingerprint_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            relativePath_,
            nameof(relativePath_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sha256Fingerprint_,
            nameof(sha256Fingerprint_));

        if (byteLength_ < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteLength_),
                byteLength_,
                "Byte length cannot be negative.");
        }

        if (sha256Fingerprint_.Length != 64 ||
            !IsLowerHex(sha256Fingerprint_))
        {
            throw new ArgumentException(
                "SHA-256 fingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(sha256Fingerprint_));
        }

        this.RelativePath =
            relativePath_;
        this.ByteLength =
            byteLength_;
        this.Sha256Fingerprint =
            sha256Fingerprint_;
    }

    private static bool IsLowerHex(
        string value_)
    {
        for (int i = 0; i < value_.Length; i++)
        {
            char c = value_[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
