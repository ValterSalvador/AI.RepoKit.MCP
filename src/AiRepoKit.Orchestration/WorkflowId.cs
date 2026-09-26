namespace AiRepoKit.Orchestration;

using System.Text;

public sealed record WorkflowId
{
    private const int MaximumUtf8ByteCount = 64;

    private static readonly UTF8Encoding _strictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    public string Value
    {
        get;
    }

    public WorkflowId(
        string value_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            value_,
            nameof(value_));

        int byteCount;

        try
        {
            byteCount =
                _strictUtf8.GetByteCount(
                    value_);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException(
                "Workflow ID must contain valid Unicode text.",
                nameof(value_),
                exception);
        }

        if (byteCount >
            MaximumUtf8ByteCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value_),
                value_,
                $"Workflow ID UTF-8 representation must not exceed {MaximumUtf8ByteCount} bytes.");
        }

        this.Value =
            value_;
    }
}
