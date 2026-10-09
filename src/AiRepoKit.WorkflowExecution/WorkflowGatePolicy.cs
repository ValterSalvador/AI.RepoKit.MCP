namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;

public sealed record WorkflowGatePolicy
{
    public bool RequireGateForReadOnly
    {
        get;
    }

    public bool RequireGateForWorkspaceWrite
    {
        get;
    }

    public bool RequireGateForUnrestricted
    {
        get;
    }

    public WorkflowGatePolicy(
        bool requireGateForReadOnly_,
        bool requireGateForWorkspaceWrite_,
        bool requireGateForUnrestricted_)
    {
        this.RequireGateForReadOnly = requireGateForReadOnly_;
        this.RequireGateForWorkspaceWrite = requireGateForWorkspaceWrite_;
        this.RequireGateForUnrestricted = requireGateForUnrestricted_;
    }

    public bool RequiresGate(
        ExecutionPermission permission_)
    {
        return permission_ switch
        {
            ExecutionPermission.ReadOnly => this.RequireGateForReadOnly,
            ExecutionPermission.WorkspaceWrite => this.RequireGateForWorkspaceWrite,
            ExecutionPermission.Unrestricted => this.RequireGateForUnrestricted,
            _ => throw new ArgumentOutOfRangeException(
                nameof(permission_),
                permission_,
                "Unsupported or undefined execution permission.")
        };
    }
}
