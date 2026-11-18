namespace AiRepoKit.WorkflowExecution;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class WorkflowBrokerSecurityPrimitives
{
    public static string GenerateToken()
    {
        byte[] bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

internal static class WorkflowBrokerProtocol
{
    public const string ProtocolVersion = "ai.repokit.broker-ipc/v1";

    public const string ActionPing = "Ping";
    public const string ActionAcquireLease = "AcquireLease";
    public const string ActionReleaseLease = "ReleaseLease";
    public const string ActionQuarantineLease = "QuarantineLease";
    public const string ActionAcquireMaintenanceLease = "AcquireMaintenanceLease";
    public const string ActionReleaseMaintenanceLease = "ReleaseMaintenanceLease";
    public const string ActionInspectIntegrity = "InspectIntegrity";

    public const string ErrorCodeUnauthorized = "UNAUTHORIZED";
    public const string ErrorCodeConflict = "CONFLICT";
    public const string ErrorCodeNotFound = "NOT_FOUND";
    public const string ErrorCodeTimeout = "TIMEOUT";
    public const string ErrorCodeInvalidRequest = "INVALID_REQUEST";
    public const string ErrorCodeInternalError = "INTERNAL_ERROR";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static async Task SendMessageAsync(
        Stream stream_,
        object message_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(stream_, nameof(stream_));
        ArgumentNullException.ThrowIfNull(message_, nameof(message_));

        byte[] payloadBytes = JsonSerializer.SerializeToUtf8Bytes(message_, message_.GetType(), _jsonOptions);
        byte[] lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payloadBytes.Length);

        await stream_.WriteAsync(lengthPrefix, cancellationToken_).ConfigureAwait(false);
        await stream_.WriteAsync(payloadBytes, cancellationToken_).ConfigureAwait(false);
        await stream_.FlushAsync(cancellationToken_).ConfigureAwait(false);
    }

    public static async Task<T?> ReceiveMessageAsync<T>(
        Stream stream_,
        CancellationToken cancellationToken_ = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(stream_, nameof(stream_));

        byte[] lengthPrefix = new byte[4];
        int bytesRead = await ReadExactAsync(stream_, lengthPrefix, cancellationToken_).ConfigureAwait(false);
        if (bytesRead == 0)
        {
            return null; // Connection closed gracefully
        }

        if (bytesRead < 4)
        {
            throw new InvalidOperationException("Truncated length prefix received from IPC peer.");
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (payloadLength <= 0 || payloadLength > 16 * 1024 * 1024)
        {
            throw new InvalidOperationException($"Invalid payload length '{payloadLength}' received from IPC peer.");
        }

        byte[] payloadBuffer = new byte[payloadLength];
        int payloadRead = await ReadExactAsync(stream_, payloadBuffer, cancellationToken_).ConfigureAwait(false);
        if (payloadRead < payloadLength)
        {
            throw new InvalidOperationException($"Incomplete payload read from IPC peer (expected {payloadLength}, got {payloadRead}).");
        }

        return JsonSerializer.Deserialize<T>(payloadBuffer, _jsonOptions);
    }

    private static async Task<int> ReadExactAsync(
        Stream stream_,
        byte[] buffer_,
        CancellationToken cancellationToken_)
    {
        int totalRead = 0;
        while (totalRead < buffer_.Length)
        {
            int read = await stream_.ReadAsync(
                buffer_.AsMemory(totalRead, buffer_.Length - totalRead),
                cancellationToken_).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }
}

internal sealed class WorkflowBrokerRequest
{
    public string RequestId { get; init; } = WorkflowBrokerSecurityPrimitives.GenerateToken();
    public string Action { get; init; } = string.Empty;
    public string? WorkflowId { get; init; }
    public string? OwnerToken { get; init; }
    public string? DomainMode { get; init; }
    public List<WorkflowBrokerResourceClaimDto>? ResourceClaims { get; init; }
    public int TimeoutMs { get; init; } = 5000;
    public int MaxConcurrentWorkflows { get; init; } = 4;
    public string? Reason { get; init; }
}

internal sealed class WorkflowBrokerResponse
{
    public string RequestId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public WorkflowBrokerLeaseDto? Lease { get; init; }
    public WorkflowBrokerIntegrityReportDto? IntegrityReport { get; init; }

    public static WorkflowBrokerResponse Ok(string requestId_, WorkflowBrokerLeaseDto? lease_ = null) =>
        new()
        {
            RequestId = requestId_,
            Success = true,
            Lease = lease_
        };

    public static WorkflowBrokerResponse OkIntegrity(string requestId_, WorkflowBrokerIntegrityReportDto report_) =>
        new()
        {
            RequestId = requestId_,
            Success = true,
            IntegrityReport = report_
        };

    public static WorkflowBrokerResponse Error(string requestId_, string errorCode_, string errorMessage_) =>
        new()
        {
            RequestId = requestId_,
            Success = false,
            ErrorCode = errorCode_,
            ErrorMessage = errorMessage_
        };
}

internal sealed class WorkflowBrokerResourceClaimDto
{
    public string ResourceId { get; init; } = string.Empty;
    public int AccessKind { get; init; }
}

internal sealed class WorkflowBrokerLeaseDto
{
    public string WorkflowId { get; init; } = string.Empty;
    public string OwnerToken { get; init; } = string.Empty;
    public int Slot { get; init; }
    public string DomainMode { get; init; } = string.Empty;
    public List<WorkflowBrokerResourceClaimDto> ResourceClaims { get; init; } = [];
    public string? CreatedAt { get; init; }
}

internal sealed class WorkflowBrokerIntegrityReportDto
{
    public bool IsHealthy { get; init; }
    public bool HasGlobalExclusion { get; init; }
    public int ActiveLeaseCount { get; init; }
    public int QuarantinedLeaseCount { get; init; }
    public int MaintenanceLeaseCount { get; init; }
    public List<string> Violations { get; init; } = [];
}
