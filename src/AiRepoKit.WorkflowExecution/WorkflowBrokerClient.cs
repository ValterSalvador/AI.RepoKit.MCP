namespace AiRepoKit.WorkflowExecution;

using System.Globalization;
using System.IO.Pipes;
using System.Net.Sockets;

internal sealed class WorkflowBrokerException : Exception
{
    public string ErrorCode { get; }

    public WorkflowBrokerException(string errorCode_, string message_)
        : base(message_)
    {
        this.ErrorCode = errorCode_ ?? string.Empty;
    }
}

internal sealed class WorkflowBrokerMaintenanceHandle : IAsyncDisposable, IDisposable
{
    private readonly WorkflowBrokerClient _client;
    private bool _isCompleted;
    private bool _isDisposed;

    public string WorkflowId { get; }
    public string OwnerToken { get; }

    public WorkflowBrokerMaintenanceHandle(
        WorkflowBrokerClient client_,
        string workflowId_,
        string ownerToken_)
    {
        this._client = client_ ?? throw new ArgumentNullException(nameof(client_));
        this.WorkflowId = workflowId_ ?? throw new ArgumentNullException(nameof(workflowId_));
        this.OwnerToken = ownerToken_ ?? throw new ArgumentNullException(nameof(ownerToken_));
    }

    public async Task CompleteAsync(CancellationToken cancellationToken_ = default)
    {
        if (this._isDisposed)
        {
            throw new ObjectDisposedException(nameof(WorkflowBrokerMaintenanceHandle));
        }

        if (this._isCompleted)
        {
            return;
        }

        await this._client.ReleaseMaintenanceLeaseAsync(this.WorkflowId, this.OwnerToken, cancellationToken_).ConfigureAwait(false);
        this._isCompleted = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;

        // If disposed without explicit completion, fail closed:
        // Do NOT release or downgrade the MaintenanceInProgress manifest!
        // Preserve MaintenanceInProgress and ExclusiveMaintenance global exclusion.
        await Task.CompletedTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;

        // If disposed without explicit completion, fail closed:
        // Do NOT downgrade active maintenance to Quarantined/Normal!
        // Preserve MaintenanceInProgress and ExclusiveMaintenance global exclusion.
    }
}

internal sealed class WorkflowBrokerLeaseHandle : IAsyncDisposable, IDisposable
{
    private readonly WorkflowBrokerClient _client;
    private bool _isDisposed;

    public string WorkflowId { get; }
    public string OwnerToken { get; }
    public int Slot { get; }
    public string DomainMode { get; }
    public IReadOnlyList<WorkflowResourceClaim> ResourceClaims { get; }

    public WorkflowBrokerLeaseHandle(
        WorkflowBrokerClient client_,
        WorkflowBrokerLeaseDto leaseDto_)
    {
        this._client = client_ ?? throw new ArgumentNullException(nameof(client_));
        ArgumentNullException.ThrowIfNull(leaseDto_, nameof(leaseDto_));

        this.WorkflowId = leaseDto_.WorkflowId;
        this.OwnerToken = leaseDto_.OwnerToken;
        this.Slot = leaseDto_.Slot;
        this.DomainMode = leaseDto_.DomainMode;
        this.ResourceClaims = leaseDto_.ResourceClaims
            .Select(c => new WorkflowResourceClaim(c.ResourceId, (WorkflowResourceAccessKind) c.AccessKind))
            .ToList();
    }

    public async ValueTask DisposeAsync()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        try
        {
            await this._client.ReleaseLeaseAsync(this.WorkflowId, this.OwnerToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        try
        {
            this._client.ReleaseLeaseAsync(this.WorkflowId, this.OwnerToken).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}

internal sealed class WorkflowBrokerClient : IAsyncDisposable, IDisposable
{
    private readonly WorkflowSchedulerPaths _paths;
    private readonly Stream _stream;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private bool _isDisposed;

    public Stream Stream => this._stream;

    private WorkflowBrokerClient(
        WorkflowSchedulerPaths paths_,
        Stream stream_)
    {
        this._paths = paths_;
        this._stream = stream_;
    }

    public static async Task<WorkflowBrokerClient> ConnectAsync(
        WorkflowSchedulerPaths paths_,
        string? expectedServerIdentity_ = null,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(paths_, nameof(paths_));
        // Worker-side bootstrap isolation: do NOT mutate or initialize protected locks layout.
        // The coordination broker alone is responsible for establishing coordination metadata.

        Stream stream;
        if (OperatingSystem.IsWindows())
        {
            string pipeName = WorkflowBrokerIpcTransport.GetEndpointIdentifier(paths_);
            NamedPipeClientStream pipeClient = new(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            using (CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(5)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken_, timeoutCts.Token))
            {
                await pipeClient.ConnectAsync(linked.Token).ConfigureAwait(false);
            }

            if (!WorkflowBrokerIpcTransport.VerifyServerIdentity(pipeClient, paths_, expectedServerIdentity_))
            {
                pipeClient.Dispose();
                throw new InvalidOperationException("Failed to verify Broker server process identity; possible rogue broker.");
            }

            stream = pipeClient;
        }
        else
        {
            string socketPath = WorkflowBrokerIpcTransport.GetEndpointIdentifier(paths_);
            string? expectedBrokerIdentity = expectedServerIdentity_ ?? WorkflowBrokerIpcTransport.DefaultBrokerServicePrincipal;
            if (string.IsNullOrWhiteSpace(expectedBrokerIdentity) ||
                !uint.TryParse(expectedBrokerIdentity, NumberStyles.None, CultureInfo.InvariantCulture, out uint trustedBrokerUid))
            {
                throw new InvalidOperationException("Failed to validate Linux socket security: missing or invalid trusted broker UID service identity.");
            }

            if (!WorkflowBrokerIpcTransport.ValidateLinuxSocketSecurity(socketPath, paths_.IpcDirectory, trustedBrokerUid))
            {
                throw new InvalidOperationException("Failed to validate Linux socket security permissions (must be owned by trusted broker principal without group/other write access on directory or execute on socket).");
            }

            Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), cancellationToken_).ConfigureAwait(false);

            NetworkStream netStream = new(socket, ownsSocket: true);
            if (!WorkflowBrokerIpcTransport.VerifyServerIdentity(netStream, paths_, expectedServerIdentity_))
            {
                netStream.Dispose();
                throw new InvalidOperationException("Failed to verify Broker server peer credentials; possible rogue broker.");
            }

            stream = netStream;
        }

        WorkflowBrokerClient client = new(paths_, stream);
        await client.PingAsync(cancellationToken_).ConfigureAwait(false);
        return client;
    }

    public async Task PingAsync(CancellationToken cancellationToken_ = default)
    {
        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionPing
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeInternalError,
                response.ErrorMessage ?? "Ping failed.");
        }
    }

    public async Task<WorkflowBrokerLeaseHandle> AcquireLeaseAsync(
        string workflowId_,
        IEnumerable<WorkflowResourceClaim> claims_,
        string domainMode_ = "Normal",
        TimeSpan? timeout_ = null,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId_, nameof(workflowId_));
        ArgumentNullException.ThrowIfNull(claims_, nameof(claims_));

        List<WorkflowBrokerResourceClaimDto> claimDtos = claims_.Select(c => new WorkflowBrokerResourceClaimDto
        {
            ResourceId = c.ResourceId,
            AccessKind = (int) c.AccessKind
        }).ToList();

        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionAcquireLease,
            WorkflowId = workflowId_,
            DomainMode = domainMode_,
            ResourceClaims = claimDtos,
            TimeoutMs = timeout_.HasValue ? (int) timeout_.Value.TotalMilliseconds : 5000
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success || response.Lease is null)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeConflict,
                response.ErrorMessage ?? $"Failed to acquire lease for workflow '{workflowId_}'.");
        }

        return new WorkflowBrokerLeaseHandle(this, response.Lease);
    }

    public async Task<WorkflowBrokerMaintenanceHandle> AcquireMaintenanceLeaseAsync(
        string workflowId_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId_, nameof(workflowId_));

        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionAcquireMaintenanceLease,
            WorkflowId = workflowId_
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success || response.Lease is null)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeConflict,
                response.ErrorMessage ?? $"Failed to acquire maintenance lease for workflow '{workflowId_}'.");
        }

        return new WorkflowBrokerMaintenanceHandle(this, workflowId_, response.Lease.OwnerToken);
    }

    public async Task ReleaseMaintenanceLeaseAsync(
        string workflowId_,
        string ownerToken_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId_, nameof(workflowId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken_, nameof(ownerToken_));

        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionReleaseMaintenanceLease,
            WorkflowId = workflowId_,
            OwnerToken = ownerToken_
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeUnauthorized,
                response.ErrorMessage ?? $"Failed to release maintenance lease for workflow '{workflowId_}'.");
        }
    }

    public async Task ReleaseLeaseAsync(
        string workflowId_,
        string ownerToken_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId_, nameof(workflowId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken_, nameof(ownerToken_));

        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionReleaseLease,
            WorkflowId = workflowId_,
            OwnerToken = ownerToken_
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeUnauthorized,
                response.ErrorMessage ?? $"Failed to release lease for workflow '{workflowId_}'.");
        }
    }

    public async Task QuarantineLeaseAsync(
        string workflowId_,
        string ownerToken_,
        string reason_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowId_, nameof(workflowId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToken_, nameof(ownerToken_));

        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionQuarantineLease,
            WorkflowId = workflowId_,
            OwnerToken = ownerToken_,
            Reason = reason_
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeInternalError,
                response.ErrorMessage ?? $"Failed to quarantine lease for workflow '{workflowId_}'.");
        }
    }

    public async Task<WorkflowBrokerIntegrityReportDto> InspectIntegrityAsync(
        int maxConcurrentWorkflows_ = 4,
        CancellationToken cancellationToken_ = default)
    {
        WorkflowBrokerRequest request = new()
        {
            Action = WorkflowBrokerProtocol.ActionInspectIntegrity,
            MaxConcurrentWorkflows = maxConcurrentWorkflows_
        };

        WorkflowBrokerResponse response = await this.SendRequestAsync(request, cancellationToken_).ConfigureAwait(false);
        if (!response.Success || response.IntegrityReport is null)
        {
            throw new WorkflowBrokerException(
                response.ErrorCode ?? WorkflowBrokerProtocol.ErrorCodeInternalError,
                response.ErrorMessage ?? "Failed to inspect integrity.");
        }

        return response.IntegrityReport;
    }

    private async Task<WorkflowBrokerResponse> SendRequestAsync(
        WorkflowBrokerRequest request_,
        CancellationToken cancellationToken_)
    {
        await this._sendGate.WaitAsync(cancellationToken_).ConfigureAwait(false);
        try
        {
            await WorkflowBrokerProtocol.SendMessageAsync(this._stream, request_, cancellationToken_).ConfigureAwait(false);
            WorkflowBrokerResponse? response = await WorkflowBrokerProtocol.ReceiveMessageAsync<WorkflowBrokerResponse>(
                this._stream,
                cancellationToken_).ConfigureAwait(false);

            if (response is null)
            {
                throw new InvalidOperationException("Broker closed the IPC connection unexpectedly.");
            }

            return response;
        }
        finally
        {
            this._sendGate.Release();
        }
    }

    public void Dispose()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        this._stream.Dispose();
        this._sendGate.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (this._isDisposed)
        {
            return;
        }

        this._isDisposed = true;
        await this._stream.DisposeAsync().ConfigureAwait(false);
        this._sendGate.Dispose();
    }
}
