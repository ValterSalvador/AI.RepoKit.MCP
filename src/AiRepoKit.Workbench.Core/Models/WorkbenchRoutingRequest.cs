namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Input request for workbench intent routing and policy resolution.
/// </summary>
public sealed class WorkbenchRoutingRequest
{
    public const string DefaultContractVersion = "wb.routing/v1";

    public string ContractVersion { get; }
    public string RepositoryId { get; }
    public string ObservedBaselineSha { get; }
    public string? TargetBaselineSha { get; }
    public string? Intent { get; }
    public string? TaskDescription { get; }
    public WorkbenchStructuredContext? StructuredContext { get; }

    public WorkbenchRoutingRequest(
        string repositoryId_,
        string observedBaselineSha_,
        string? intent_ = null,
        string? taskDescription_ = null,
        WorkbenchStructuredContext? structuredContext_ = null,
        string? targetBaselineSha_ = null,
        string contractVersion_ = DefaultContractVersion)
    {
        this.ContractVersion = contractVersion_ ?? DefaultContractVersion;
        this.RepositoryId = repositoryId_ ?? string.Empty;
        this.ObservedBaselineSha = observedBaselineSha_ ?? string.Empty;
        this.TargetBaselineSha = targetBaselineSha_;
        this.Intent = intent_;
        this.TaskDescription = taskDescription_;
        this.StructuredContext = structuredContext_;
    }
}
