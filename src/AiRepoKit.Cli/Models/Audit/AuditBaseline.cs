using System.Text.Json.Serialization;

namespace AiRepoKit.Cli.Models.Audit;

public sealed record AuditBaseline
{
    [JsonConstructor]
    public AuditBaseline(string version, DateTimeOffset generatedAtLocal, IReadOnlyList<AuditBaselineEntry> entries)
    {
        this.Version = version;
        this.GeneratedAtLocal = generatedAtLocal;
        this.Entries = entries;
    }

    public string Version { get; }

    public DateTimeOffset GeneratedAtLocal { get; }

    public IReadOnlyList<AuditBaselineEntry> Entries { get; }
}
