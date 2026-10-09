namespace AiRepoKit.WorkflowExecution;

using System.Text;

public sealed record WorkflowGateProof
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public string GateId
    {
        get;
    }

    public string OpaqueEvidence
    {
        get;
    }

    public WorkflowGateProof(
        string gateId_,
        string opaqueEvidence_)
    {
        ArgumentNullException.ThrowIfNull(
            gateId_,
            nameof(gateId_));
        ArgumentNullException.ThrowIfNull(
            opaqueEvidence_,
            nameof(opaqueEvidence_));

        if (!WorkflowGateChallenge.IsLowerHex64(gateId_))
        {
            throw new ArgumentException(
                "GateId must be exactly 64 lowercase hexadecimal characters.",
                nameof(gateId_));
        }

        if (string.IsNullOrWhiteSpace(opaqueEvidence_))
        {
            throw new ArgumentException(
                "Opaque evidence must be non-null and non-whitespace.",
                nameof(opaqueEvidence_));
        }

        int byteCount;
        try
        {
            byteCount = _strictUtf8.GetByteCount(opaqueEvidence_);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Opaque evidence contains invalid Unicode characters.",
                nameof(opaqueEvidence_),
                exception);
        }

        if (byteCount > 65536)
        {
            throw new ArgumentException(
                $"Opaque evidence UTF-8 size ({byteCount} bytes) exceeds the maximum allowed size of 65536 bytes.",
                nameof(opaqueEvidence_));
        }

        this.GateId = gateId_;
        this.OpaqueEvidence = opaqueEvidence_;
    }
}
