namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Orchestration;

public sealed record WorkflowEvidencePack
{
    public string PackId
    {
        get;
    }

    public WorkflowId WorkflowId
    {
        get;
    }

    public long CapturedPersistenceRevision
    {
        get;
    }

    public string ManifestFingerprint
    {
        get;
    }

    public string PackDirectory
    {
        get;
    }

    public string ManifestPath
    {
        get;
    }

    public WorkflowEvidenceManifest Manifest
    {
        get;
    }

    internal WorkflowEvidencePack(
        string packId_,
        WorkflowId workflowId_,
        long capturedPersistenceRevision_,
        string manifestFingerprint_,
        string packDirectory_,
        string manifestPath_,
        WorkflowEvidenceManifest manifest_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            packId_,
            nameof(packId_));
        ArgumentNullException.ThrowIfNull(
            workflowId_,
            nameof(workflowId_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            manifestFingerprint_,
            nameof(manifestFingerprint_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            packDirectory_,
            nameof(packDirectory_));
        ArgumentException.ThrowIfNullOrWhiteSpace(
            manifestPath_,
            nameof(manifestPath_));
        ArgumentNullException.ThrowIfNull(
            manifest_,
            nameof(manifest_));

        if (capturedPersistenceRevision_ <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capturedPersistenceRevision_),
                capturedPersistenceRevision_,
                "Captured persistence revision must be greater than zero.");
        }

        if (manifestFingerprint_.Length != 64)
        {
            throw new ArgumentException(
                "Manifest fingerprint must be exactly 64 lowercase hexadecimal characters.",
                nameof(manifestFingerprint_));
        }

        this.PackId =
            packId_;
        this.WorkflowId =
            workflowId_;
        this.CapturedPersistenceRevision =
            capturedPersistenceRevision_;
        this.ManifestFingerprint =
            manifestFingerprint_;
        this.PackDirectory =
            packDirectory_;
        this.ManifestPath =
            manifestPath_;
        this.Manifest =
            manifest_;
    }
}
