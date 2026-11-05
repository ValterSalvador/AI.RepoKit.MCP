namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Evidence of an existing authorization or gate approval.
/// </summary>
public sealed class WorkbenchApproval
{
    public string ApprovalId { get; }
    public string ApprovedBaselineSha { get; }
    public string Role { get; }
    public bool IsStale { get; }
    public DateTimeOffset? ApprovedAt { get; }

    public WorkbenchApproval(
        string approvalId_,
        string approvedBaselineSha_,
        string role_,
        bool isStale_ = false,
        DateTimeOffset? approvedAt_ = null)
    {
        this.ApprovalId = approvalId_ ?? string.Empty;
        this.ApprovedBaselineSha = approvedBaselineSha_ ?? string.Empty;
        this.Role = role_ ?? string.Empty;
        this.IsStale = isStale_;
        this.ApprovedAt = approvedAt_;
    }
}
