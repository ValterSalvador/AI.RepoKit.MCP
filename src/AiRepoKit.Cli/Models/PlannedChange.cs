using AiRepoKit.Cli.Models.ManagedFiles;

namespace AiRepoKit.Cli.Models;

public sealed record PlannedChange
{
    public PlannedChange(
        ChangeType changeType_,
        string path_,
        string description_,
        bool willWrite_,
        bool exists_,
        bool requiresBackup_,
        bool isSensitive_,
        string reason_)
        : this(changeType_, path_, description_, willWrite_, exists_, requiresBackup_, isSensitive_, reason_, string.Empty, string.Empty, exists_ ? GeneratedFileState.UnmanagedExisting : GeneratedFileState.Missing, UpdateAction.Skip, false, false, string.Empty, string.Empty, false)
    {
    }

    public PlannedChange(
        ChangeType changeType_,
        string path_,
        string description_,
        bool willWrite_,
        bool exists_,
        bool requiresBackup_,
        bool isSensitive_,
        string reason_,
        string templateId_,
        string templateVersion_,
        GeneratedFileState generatedFileState_,
        UpdateAction updateAction_,
        bool requiresManualReview_,
        bool hasDiff_,
        string currentHash_,
        string proposedHash_,
        bool managedByManifest_)
    {
        this.ChangeType = changeType_;
        this.Path = path_;
        this.Description = description_;
        this.WillWrite = willWrite_;
        this.Exists = exists_;
        this.RequiresBackup = requiresBackup_;
        this.IsSensitive = isSensitive_;
        this.Reason = reason_;
        this.TemplateId = templateId_;
        this.TemplateVersion = templateVersion_;
        this.GeneratedFileState = generatedFileState_;
        this.UpdateAction = updateAction_;
        this.RequiresManualReview = requiresManualReview_;
        this.HasDiff = hasDiff_;
        this.CurrentHash = currentHash_;
        this.ProposedHash = proposedHash_;
        this.ManagedByManifest = managedByManifest_;
    }

    public ChangeType ChangeType { get; }

    public string Path { get; }

    public string Description { get; }

    public bool WillWrite { get; }

    public bool Exists { get; }

    public bool RequiresBackup { get; }

    public bool IsSensitive { get; }

    public string Reason { get; }

    public string TemplateId { get; }

    public string TemplateVersion { get; }

    public GeneratedFileState GeneratedFileState { get; }

    public UpdateAction UpdateAction { get; }

    public bool RequiresManualReview { get; }

    public bool HasDiff { get; }

    public string CurrentHash { get; }

    public string ProposedHash { get; }

    public bool ManagedByManifest { get; }
}
