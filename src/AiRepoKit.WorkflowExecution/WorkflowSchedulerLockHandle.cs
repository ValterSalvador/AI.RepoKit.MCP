namespace AiRepoKit.WorkflowExecution;

using Microsoft.Win32.SafeHandles;

internal sealed class WorkflowLockExclusiveCapability
{
    private readonly SafeFileHandle _boundHandle;
    private readonly string _boundPath;

    internal WorkflowLockExclusiveCapability(
        SafeFileHandle boundHandle_,
        string boundPath_)
    {
        ArgumentNullException.ThrowIfNull(
            boundHandle_,
            nameof(boundHandle_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            boundPath_,
            nameof(boundPath_));

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                boundHandle_))
        {
            throw new InvalidOperationException(
                "Cannot construct exclusive capability: file handle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireExclusive.");
        }

        this._boundHandle =
            boundHandle_;
        this._boundPath =
            System.IO.Path.GetFullPath(
                boundPath_);
    }

    internal bool IsBoundTo(
        FileStream? stream_,
        string path_)
    {
        if (stream_ is null ||
            stream_.SafeFileHandle.IsInvalid ||
            stream_.SafeFileHandle.IsClosed)
        {
            return false;
        }

        if (this._boundHandle.IsInvalid ||
            this._boundHandle.IsClosed)
        {
            return false;
        }

        if (!ReferenceEquals(
                this._boundHandle,
                stream_.SafeFileHandle))
        {
            return false;
        }

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                this._boundHandle))
        {
            return false;
        }

        StringComparison comp =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return string.Equals(
            this._boundPath,
            System.IO.Path.GetFullPath(
                path_),
            comp);
    }
}

internal sealed class WorkflowLockSharedCapability
{
    private readonly SafeFileHandle _boundHandle;
    private readonly string _boundPath;

    internal WorkflowLockSharedCapability(
        SafeFileHandle boundHandle_,
        string boundPath_)
    {
        ArgumentNullException.ThrowIfNull(
            boundHandle_,
            nameof(boundHandle_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            boundPath_,
            nameof(boundPath_));

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredShared(
                boundHandle_))
        {
            throw new InvalidOperationException(
                "Cannot construct shared capability: file handle was not genuinely acquired via WorkflowSchedulerLockPrimitives.TryAcquireShared.");
        }

        this._boundHandle =
            boundHandle_;
        this._boundPath =
            System.IO.Path.GetFullPath(
                boundPath_);
    }

    internal bool IsBoundTo(
        FileStream? stream_,
        string path_)
    {
        if (stream_ is null ||
            stream_.SafeFileHandle.IsInvalid ||
            stream_.SafeFileHandle.IsClosed)
        {
            return false;
        }

        if (this._boundHandle.IsInvalid ||
            this._boundHandle.IsClosed)
        {
            return false;
        }

        if (!ReferenceEquals(
                this._boundHandle,
                stream_.SafeFileHandle))
        {
            return false;
        }

        if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredShared(
                this._boundHandle))
        {
            return false;
        }

        StringComparison comp =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return string.Equals(
            this._boundPath,
            System.IO.Path.GetFullPath(
                path_),
            comp);
    }
}

internal sealed class WorkflowSchedulerLockHandle : IDisposable, IAsyncDisposable
{
    private FileStream? _stream;
    private int _disposed;
    private readonly bool _hasExclusiveCapability;
    private readonly WorkflowLockExclusiveCapability? _exclusiveCapability;
    private readonly bool _hasSharedCapability;
    private readonly WorkflowLockSharedCapability? _sharedCapability;

    public string Path
    {
        get;
    }

    public bool IsExclusive
    {
        get;
    }

    public bool IsDisposed =>
        this._disposed != 0;

    internal bool HasExclusiveCapability =>
        this._hasExclusiveCapability;

    internal WorkflowLockExclusiveCapability? ExclusiveCapability =>
        this._exclusiveCapability;

    internal bool HasSharedCapability =>
        this._hasSharedCapability;

    internal WorkflowLockSharedCapability? SharedCapability =>
        this._sharedCapability;

    internal FileStream? Stream =>
        this._stream;

    internal SafeFileHandle? SafeFileHandle =>
        this._stream?.SafeFileHandle;

    public WorkflowSchedulerLockHandle(
        FileStream stream_,
        string path_,
        bool isExclusive_)
        : this(stream_, path_, isExclusive_, null, null)
    {
    }

    internal WorkflowSchedulerLockHandle(
        FileStream stream_,
        string path_,
        bool isExclusive_,
        WorkflowLockExclusiveCapability? exclusiveCapability_,
        WorkflowLockSharedCapability? sharedCapability_ = null)
    {
        ArgumentNullException.ThrowIfNull(
            stream_,
            nameof(stream_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            path_,
            nameof(path_));

        if (stream_.SafeFileHandle.IsInvalid ||
            stream_.SafeFileHandle.IsClosed)
        {
            throw new ArgumentException(
                "Lock stream handle must be valid and open.",
                nameof(stream_));
        }

        if (isExclusive_)
        {
            if (exclusiveCapability_ is null)
            {
                throw new InvalidOperationException(
                    "Direct construction of exclusive lock handles from caller-provided streams is not permitted; exclusive locks must be acquired through WorkflowSchedulerLockPrimitives.");
            }

            if (!exclusiveCapability_.IsBoundTo(
                    stream_,
                    path_))
            {
                throw new InvalidOperationException(
                    "Provided exclusive lock capability is not bound to the specified stream and path.");
            }

            if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredExclusive(
                    stream_.SafeFileHandle))
            {
                throw new InvalidOperationException(
                    "Direct construction of exclusive lock handles from caller-provided streams is not permitted; exclusive locks must be acquired through WorkflowSchedulerLockPrimitives.");
            }

            if (!stream_.CanWrite)
            {
                throw new ArgumentException(
                    "Exclusive lock stream must be writable.",
                    nameof(stream_));
            }

            this._exclusiveCapability =
                exclusiveCapability_;
            this._hasExclusiveCapability =
                true;
        }
        else
        {
            if (sharedCapability_ is not null)
            {
                if (!sharedCapability_.IsBoundTo(
                        stream_,
                        path_))
                {
                    throw new InvalidOperationException(
                        "Provided shared lock capability is not bound to the specified stream and path.");
                }

                if (!WorkflowSchedulerLockPrimitives.IsGenuinelyAcquiredShared(
                        stream_.SafeFileHandle))
                {
                    throw new InvalidOperationException(
                        "Direct construction of shared lock handles from caller-provided streams is not permitted; shared locks must be acquired through WorkflowSchedulerLockPrimitives.");
                }

                this._sharedCapability =
                    sharedCapability_;
                this._hasSharedCapability =
                    true;
            }
        }

        WorkflowSchedulerPaths.AssertSafeOpenFile(
            stream_.SafeFileHandle,
            path_);

        this._stream =
            stream_;
        this.Path =
            path_;
        this.IsExclusive =
            isExclusive_;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref this._disposed,
                1) != 0)
        {
            return;
        }

        FileStream? stream =
            Interlocked.Exchange(
                ref this._stream,
                null);

        if (stream is not null)
        {
            // Do not suppress disposal exceptions when lock release is a precondition.
            stream.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(
                ref this._disposed,
                1) != 0)
        {
            return;
        }

        FileStream? stream =
            Interlocked.Exchange(
                ref this._stream,
                null);

        if (stream is not null)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }
}
