using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Models.Audit;

public sealed record AuditBaselineEntry
{
    [JsonConstructor]
    public AuditBaselineEntry(
        string id,
        string category,
        string file,
        int line,
        string previewHash,
        AuditBaselineStatus status,
        string reason,
        DateOnly? expiresOn,
        DateTimeOffset createdAtLocal,
        DateTimeOffset updatedAtLocal)
    {
        this.Id = id;
        this.Category = category;
        this.File = file;
        this.Line = line;
        this.PreviewHash = previewHash;
        this.Status = status;
        this.Reason = reason;
        this.ExpiresOn = expiresOn;
        this.CreatedAtLocal = createdAtLocal;
        this.UpdatedAtLocal = updatedAtLocal;
    }

    public string Id { get; }

    public string Category { get; }

    public string File { get; }

    public int Line { get; }

    public string PreviewHash { get; }

    public AuditBaselineStatus Status { get; }

    public string Reason { get; }

    public DateOnly? ExpiresOn { get; }

    public DateTimeOffset CreatedAtLocal { get; }

    public DateTimeOffset UpdatedAtLocal { get; }
}
