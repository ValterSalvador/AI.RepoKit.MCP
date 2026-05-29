namespace AiRepoKit.Cli.Models.ManagedFiles;

public sealed record ManagedFilesManifest
{
    public ManagedFilesManifest(
        string generatedAtLocal,
        string toolVersion,
        IReadOnlyList<ManagedFileEntry> files)
    {
        this.GeneratedAtLocal = generatedAtLocal;
        this.ToolVersion = toolVersion;
        this.Files = files;
    }

    public string GeneratedAtLocal { get; }

    public string ToolVersion { get; }

    public IReadOnlyList<ManagedFileEntry> Files { get; }
}
