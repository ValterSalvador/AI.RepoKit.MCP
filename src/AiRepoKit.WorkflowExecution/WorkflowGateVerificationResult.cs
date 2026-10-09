namespace AiRepoKit.WorkflowExecution;

using System.Text;

public sealed record WorkflowGateVerificationResult
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public bool IsAuthenticated
    {
        get;
    }

    public bool IsAuthorized
    {
        get;
    }

    public string? PrincipalId
    {
        get;
    }

    public string? MechanismId
    {
        get;
    }

    public string? EvidenceFingerprint
    {
        get;
    }

    public string? FailureReason
    {
        get;
    }

    public WorkflowGateVerificationResult(
        bool isAuthenticated_,
        bool isAuthorized_,
        string? principalId_,
        string? mechanismId_,
        string? evidenceFingerprint_,
        string? failureReason_)
    {
        if (!isAuthenticated_ && isAuthorized_)
        {
            throw new ArgumentException(
                "Unauthenticated result cannot be authorized.");
        }

        if (isAuthenticated_)
        {
            if (string.IsNullOrWhiteSpace(principalId_))
            {
                throw new ArgumentException(
                    "PrincipalId must be non-null and non-whitespace for authenticated results.",
                    nameof(principalId_));
            }

            int principalByteCount;
            try
            {
                principalByteCount = _strictUtf8.GetByteCount(principalId_);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "PrincipalId contains invalid Unicode characters.",
                    nameof(principalId_),
                    exception);
            }

            if (principalByteCount > 256)
            {
                throw new ArgumentException(
                    $"PrincipalId UTF-8 size ({principalByteCount} bytes) exceeds the maximum allowed size of 256 bytes.",
                    nameof(principalId_));
            }

            if (string.IsNullOrWhiteSpace(mechanismId_))
            {
                throw new ArgumentException(
                    "MechanismId must be non-null and non-whitespace for authenticated results.",
                    nameof(mechanismId_));
            }

            int mechanismByteCount;
            try
            {
                mechanismByteCount = _strictUtf8.GetByteCount(mechanismId_);
            }
            catch (EncoderFallbackException exception)
            {
                throw new ArgumentException(
                    "MechanismId contains invalid Unicode characters.",
                    nameof(mechanismId_),
                    exception);
            }

            if (mechanismByteCount > 128)
            {
                throw new ArgumentException(
                    $"MechanismId UTF-8 size ({mechanismByteCount} bytes) exceeds the maximum allowed size of 128 bytes.",
                    nameof(mechanismId_));
            }

            if (!WorkflowGateChallenge.IsLowerHex64(evidenceFingerprint_))
            {
                throw new ArgumentException(
                    "EvidenceFingerprint must be exactly 64 lowercase hexadecimal characters for authenticated results.",
                    nameof(evidenceFingerprint_));
            }

            if (isAuthorized_)
            {
                if (failureReason_ is not null)
                {
                    throw new ArgumentException(
                        "Authorized result must have a null failure reason.",
                        nameof(failureReason_));
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(failureReason_))
                {
                    throw new ArgumentException(
                        "Authenticated denial must have a non-null and non-whitespace failure reason.",
                        nameof(failureReason_));
                }

                ValidateFailureReason(failureReason_);
            }
        }
        else
        {
            if (principalId_ is not null)
            {
                throw new ArgumentException(
                    "Unauthenticated result must have a null PrincipalId.",
                    nameof(principalId_));
            }

            if (mechanismId_ is not null)
            {
                throw new ArgumentException(
                    "Unauthenticated result must have a null MechanismId.",
                    nameof(mechanismId_));
            }

            if (evidenceFingerprint_ is not null)
            {
                throw new ArgumentException(
                    "Unauthenticated result must have a null EvidenceFingerprint.",
                    nameof(evidenceFingerprint_));
            }

            if (string.IsNullOrWhiteSpace(failureReason_))
            {
                throw new ArgumentException(
                    "Unauthenticated result must have a non-null and non-whitespace failure reason.",
                    nameof(failureReason_));
            }

            ValidateFailureReason(failureReason_);
        }

        this.IsAuthenticated = isAuthenticated_;
        this.IsAuthorized = isAuthorized_;
        this.PrincipalId = principalId_;
        this.MechanismId = mechanismId_;
        this.EvidenceFingerprint = evidenceFingerprint_;
        this.FailureReason = failureReason_;
    }

    private static void ValidateFailureReason(string failureReason_)
    {
        int failureReasonByteCount;
        try
        {
            failureReasonByteCount = _strictUtf8.GetByteCount(failureReason_);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "FailureReason contains invalid Unicode characters.",
                nameof(failureReason_),
                exception);
        }

        if (failureReasonByteCount > 1024)
        {
            throw new ArgumentException(
                $"FailureReason UTF-8 size ({failureReasonByteCount} bytes) exceeds the maximum allowed size of 1024 bytes.",
                nameof(failureReason_));
        }
    }
}
