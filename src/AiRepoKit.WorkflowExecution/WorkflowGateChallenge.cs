namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Orchestration;

public sealed record WorkflowGateChallenge
{
    public string GateId
    {
        get;
    }

    public WorkflowId WorkflowId
    {
        get;
    }

    public string TaskId
    {
        get;
    }

    public long BasePersistenceRevision
    {
        get;
    }

    public int SourceImplementationPlanRevision
    {
        get;
    }

    public ExecutionPermission RequiredPermission
    {
        get;
    }

    public long GateOrdinal
    {
        get;
    }

    public string BaseStateFingerprint
    {
        get;
    }

    public string InputFingerprint
    {
        get;
    }

    public string RegistryFingerprint
    {
        get;
    }

    public string PolicyFingerprint
    {
        get;
    }

    internal WorkflowGateChallenge(
        string gateId_,
        WorkflowId workflowId_,
        string taskId_,
        long basePersistenceRevision_,
        int sourceImplementationPlanRevision_,
        ExecutionPermission requiredPermission_,
        long gateOrdinal_,
        string baseStateFingerprint_,
        string inputFingerprint_,
        string registryFingerprint_,
        string policyFingerprint_)
    {
        ArgumentNullException.ThrowIfNull(
            gateId_,
            nameof(gateId_));
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));
        ArgumentNullException.ThrowIfNull(
            taskId_,
            nameof(taskId_));
        ArgumentNullException.ThrowIfNull(
            baseStateFingerprint_,
            nameof(baseStateFingerprint_));
        ArgumentNullException.ThrowIfNull(
            inputFingerprint_,
            nameof(inputFingerprint_));
        ArgumentNullException.ThrowIfNull(
            registryFingerprint_,
            nameof(registryFingerprint_));
        ArgumentNullException.ThrowIfNull(
            policyFingerprint_,
            nameof(policyFingerprint_));

        if (!IsLowerHex64(gateId_))
        {
            throw new ArgumentException(
                "GateId must be exactly 64 lowercase hexadecimal characters.",
                nameof(gateId_));
        }

        if (string.IsNullOrWhiteSpace(taskId_))
        {
            throw new ArgumentException(
                "TaskId must be non-empty and non-whitespace.",
                nameof(taskId_));
        }

        if (basePersistenceRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(basePersistenceRevision_),
                basePersistenceRevision_,
                "Base persistence revision must be greater than zero.");
        }

        if (sourceImplementationPlanRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceImplementationPlanRevision_),
                sourceImplementationPlanRevision_,
                "Source implementation plan revision must be greater than zero.");
        }

        if (!Enum.IsDefined(typeof(ExecutionPermission), requiredPermission_))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredPermission_),
                requiredPermission_,
                "Required permission must be a defined ExecutionPermission value.");
        }

        if (gateOrdinal_ != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(gateOrdinal_),
                gateOrdinal_,
                "Gate ordinal in V7.P10 must be exactly 1.");
        }

        if (!IsLowerHex64(baseStateFingerprint_))
        {
            throw new ArgumentException(
                "BaseStateFingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(baseStateFingerprint_));
        }

        if (!IsLowerHex64(inputFingerprint_))
        {
            throw new ArgumentException(
                "InputFingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(inputFingerprint_));
        }

        if (!IsLowerHex64(registryFingerprint_))
        {
            throw new ArgumentException(
                "RegistryFingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(registryFingerprint_));
        }

        if (!IsLowerHex64(policyFingerprint_))
        {
            throw new ArgumentException(
                "PolicyFingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(policyFingerprint_));
        }

        this.GateId = gateId_;
        this.WorkflowId = workflowId_;
        this.TaskId = taskId_;
        this.BasePersistenceRevision = basePersistenceRevision_;
        this.SourceImplementationPlanRevision = sourceImplementationPlanRevision_;
        this.RequiredPermission = requiredPermission_;
        this.GateOrdinal = gateOrdinal_;
        this.BaseStateFingerprint = baseStateFingerprint_;
        this.InputFingerprint = inputFingerprint_;
        this.RegistryFingerprint = registryFingerprint_;
        this.PolicyFingerprint = policyFingerprint_;
    }

    internal static bool IsLowerHex64(string? value_)
    {
        if (value_ is null || value_.Length != 64)
        {
            return false;
        }

        for (int index = 0; index < value_.Length; index++)
        {
            char character = value_[index];
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
