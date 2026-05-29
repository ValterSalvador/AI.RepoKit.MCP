using System.Text.Json;
using AiRepoKit.Cli.Models.ManagedFiles;

namespace AiRepoKit.Cli.Services.ManagedFiles;

public sealed class ManagedFilesService
{
    public const string ManifestRelativePath = ".ai/generated/reports/managed-files.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ContentHashService _contentHashService = new();

    public ManagedFilesManifest Load(string rootPath_)
    {
        string fullPath = this.GetManifestPath(rootPath_);
        if (!File.Exists(fullPath))
        {
            return this.CreateEmptyManifest();
        }

        try
        {
            ManagedFilesManifest? manifest = JsonSerializer.Deserialize<ManagedFilesManifest>(File.ReadAllText(fullPath), JsonOptions);
            if (manifest is null)
            {
                return this.CreateEmptyManifest();
            }

            IReadOnlyList<ManagedFileEntry> files = manifest.Files
                .Where(file_ => !string.IsNullOrWhiteSpace(file_.Path))
                .Select(file_ => new ManagedFileEntry(
                    this.NormalizeRelativePath(rootPath_, file_.Path),
                    file_.TemplateId,
                    file_.TemplateVersion,
                    file_.LastGeneratedHash,
                    file_.LastGeneratedSizeBytes,
                    file_.LastAppliedAtLocal,
                    file_.LastAction))
                .GroupBy(file_ => file_.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group_ => group_.Last())
                .OrderBy(file_ => file_.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return new ManagedFilesManifest(
                string.IsNullOrWhiteSpace(manifest.GeneratedAtLocal) ? string.Empty : manifest.GeneratedAtLocal,
                string.IsNullOrWhiteSpace(manifest.ToolVersion) ? this.GetToolVersion() : manifest.ToolVersion,
                files);
        }
        catch (IOException)
        {
            return this.CreateEmptyManifest();
        }
        catch (JsonException)
        {
            return this.CreateEmptyManifest();
        }
    }

    public void Save(string rootPath_, ManagedFilesManifest manifest_)
    {
        string fullPath = this.GetManifestPath(rootPath_);
        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        ManagedFilesManifest normalized = new(
            DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            this.GetToolVersion(),
            manifest_.Files
                .Select(file_ => new ManagedFileEntry(
                    this.NormalizeRelativePath(rootPath_, file_.Path),
                    file_.TemplateId,
                    file_.TemplateVersion,
                    file_.LastGeneratedHash,
                    file_.LastGeneratedSizeBytes,
                    file_.LastAppliedAtLocal,
                    file_.LastAction))
                .OrderBy(file_ => file_.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        File.WriteAllText(fullPath, JsonSerializer.Serialize(normalized, JsonOptions));
    }

    public ManagedFilesManifest AddOrUpdate(
        ManagedFilesManifest manifest_,
        string rootPath_,
        string relativePath_,
        string templateId_,
        string templateVersion_,
        string renderedContent_,
        string lastAction_)
    {
        string normalizedPath = this.NormalizeRelativePath(rootPath_, relativePath_);
        List<ManagedFileEntry> files = [.. manifest_.Files.Where(file_ => !string.Equals(file_.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))];
        files.Add(new ManagedFileEntry(
            normalizedPath,
            templateId_,
            templateVersion_,
            this._contentHashService.ComputeSha256(renderedContent_),
            this._contentHashService.GetSizeBytes(renderedContent_),
            DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            lastAction_));
        return new ManagedFilesManifest(
            DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            this.GetToolVersion(),
            files.OrderBy(file_ => file_.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public string NormalizeRelativePath(string rootPath_, string path_)
    {
        string rootPath = Path.GetFullPath(rootPath_);
        string candidatePath = Path.IsPathRooted(path_)
            ? Path.GetFullPath(path_)
            : Path.GetFullPath(Path.Combine(rootPath, path_));
        return Path.GetRelativePath(rootPath, candidatePath).Replace('\\', '/');
    }

    public string GetManifestPath(string rootPath_)
    {
        return Path.Combine(Path.GetFullPath(rootPath_), ManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string GetToolVersion()
    {
        return TemplateService.GetToolVersion();
    }

    private ManagedFilesManifest CreateEmptyManifest()
    {
        return new ManagedFilesManifest(string.Empty, this.GetToolVersion(), []);
    }
}
