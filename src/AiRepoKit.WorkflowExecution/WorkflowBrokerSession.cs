namespace AiRepoKit.WorkflowExecution;

internal sealed class WorkflowBrokerSession : IAsyncDisposable, IDisposable
{
    public string SessionId { get; } = WorkflowBrokerSecurityPrimitives.GenerateToken();
    public Stream Stream { get; }
    public int ClientProcessId { get; }
    public long ClientProcessStartTimeTicks { get; }
    public string ClientIdentity { get; }
    public long ConnectedAtTicks { get; } = Environment.TickCount64;

    public WorkflowBrokerLeaseDto? ActiveLease { get; set; }
    public long MaintenanceBaselineRevision { get; set; }
    public WorkflowSchedulerOwnerAuthHandle? OwnerAuthHandle { get; set; }
    public WorkflowSchedulerLockHandle? WorkflowLockHandle { get; set; }
    public WorkflowSchedulerLockHandle? SlotLockHandle { get; set; }
    public WorkflowSchedulerLockHandle? DomainModeLockHandle { get; set; }

    public WorkflowBrokerSession(
        Stream stream_,
        int clientProcessId_,
        long clientProcessStartTimeTicks_,
        string clientIdentity_)
    {
        this.Stream = stream_ ?? throw new ArgumentNullException(nameof(stream_));
        this.ClientProcessId = clientProcessId_;
        this.ClientProcessStartTimeTicks = clientProcessStartTimeTicks_;
        this.ClientIdentity = clientIdentity_ ?? string.Empty;
    }

    public void ReleaseLeaseLocks()
    {
        this.OwnerAuthHandle?.Dispose();
        this.OwnerAuthHandle = null;

        this.WorkflowLockHandle?.Dispose();
        this.WorkflowLockHandle = null;

        this.SlotLockHandle?.Dispose();
        this.SlotLockHandle = null;

        this.DomainModeLockHandle?.Dispose();
        this.DomainModeLockHandle = null;

        this.ActiveLease = null;
    }

    public void Dispose()
    {
        // Connection disposal is strictly separated from lock ownership.
        // Lock handles are managed exclusively by the coordination broker.
        try
        {
            this.Stream.Dispose();
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Connection disposal is strictly separated from lock ownership.
        // Lock handles are managed exclusively by the coordination broker.
        try
        {
            await this.Stream.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
