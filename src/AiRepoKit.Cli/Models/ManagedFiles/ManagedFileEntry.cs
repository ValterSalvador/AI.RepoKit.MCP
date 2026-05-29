namespace AiRepoKit.Cli.Models.ManagedFiles;

public sealed record ManagedFileEntry
{
    public ManagedFileEntry(
        string path,
        string templateId,
        string templateVersion,
        string lastGeneratedHash,
        long lastGeneratedSizeBytes,
        string lastAppliedAtLocal,
        string lastAction)
    {
        this.Path = path;
        this.TemplateId = templateId;
        this.TemplateVersion = templateVersion;
        this.LastGeneratedHash = lastGeneratedHash;
        this.LastGeneratedSizeBytes = lastGeneratedSizeBytes;
        this.LastAppliedAtLocal = lastAppliedAtLocal;
        this.LastAction = lastAction;
    }

    public string Path { get; }

    public string TemplateId { get; }

    public string TemplateVersion { get; }

    public string LastGeneratedHash { get; }

    public long LastGeneratedSizeBytes { get; }

    public string LastAppliedAtLocal { get; }

    public string LastAction { get; }
}
