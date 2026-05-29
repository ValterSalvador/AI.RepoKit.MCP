using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ManagedFiles;

namespace AiRepoKit.Cli.Services.ManagedFiles;

public sealed class GeneratedFileClassifier
{
    private readonly ContentHashService _contentHashService = new();
    private readonly ManagedFilesService _managedFilesService = new();

    public (
        GeneratedFileState State,
        UpdateAction Action,
        string Reason,
        string CurrentHash,
        string ProposedHash,
        bool ManagedByManifest,
        bool HasDiff) Classify(
            string rootPath_,
            string relativeDestinationPath_,
            string renderedContent_,
            string templateId_,
            string templateVersion_,
            bool exists_,
            bool isRestricted_,
            ManagedFilesManifest manifest_,
            BootstrapOptions options_)
    {
        string normalizedPath = this._managedFilesService.NormalizeRelativePath(rootPath_, relativeDestinationPath_);
        string proposedHash = this._contentHashService.ComputeSha256(renderedContent_);
        if (isRestricted_)
        {
            return (GeneratedFileState.Restricted, UpdateAction.Restricted, "Path is restricted.", string.Empty, proposedHash, false, false);
        }

        if (!exists_)
        {
            return (GeneratedFileState.Missing, UpdateAction.Create, "File will be created.", string.Empty, proposedHash, false, true);
        }

        string fullPath = Path.Combine(Path.GetFullPath(rootPath_), normalizedPath.Replace('/', Path.DirectorySeparatorChar));
        string currentHash = this._contentHashService.ComputeSha256(File.ReadAllText(fullPath));
        bool hasDiff = !string.Equals(currentHash, proposedHash, StringComparison.Ordinal);
        ManagedFileEntry? entry = manifest_.Files.FirstOrDefault(file_ => string.Equals(file_.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            if (options_.Force)
            {
                return (GeneratedFileState.UnmanagedExisting, UpdateAction.SafeUpdate, "Existing unmanaged file will be overwritten by --force.", currentHash, proposedHash, false, hasDiff);
            }

            return (GeneratedFileState.UnmanagedExisting, UpdateAction.ManualReview, "Existing file is not tracked by managed-files manifest.", currentHash, proposedHash, false, hasDiff);
        }

        if (string.Equals(currentHash, entry.LastGeneratedHash, StringComparison.Ordinal))
        {
            GeneratedFileState state = string.Equals(entry.TemplateVersion, templateVersion_, StringComparison.OrdinalIgnoreCase)
                ? GeneratedFileState.GeneratedCurrent
                : GeneratedFileState.GeneratedOutdated;
            if (!hasDiff)
            {
                return (state, UpdateAction.Skip, "Managed file already matches rendered content.", currentHash, proposedHash, true, false);
            }

            string reason = state == GeneratedFileState.GeneratedCurrent
                ? "Managed file matches the last generated hash."
                : "Managed file matches the last generated hash but template version changed.";
            return (state, UpdateAction.SafeUpdate, reason, currentHash, proposedHash, true, true);
        }

        if (options_.ForceManaged)
        {
            return (GeneratedFileState.GeneratedCustomized, UpdateAction.SafeUpdate, "Customized managed file will be overwritten by --force-managed.", currentHash, proposedHash, true, hasDiff);
        }

        if (options_.Force)
        {
            return (GeneratedFileState.GeneratedCustomized, UpdateAction.SafeUpdate, "Customized managed file will be overwritten by --force.", currentHash, proposedHash, true, hasDiff);
        }

        return (GeneratedFileState.GeneratedCustomized, UpdateAction.ManualReview, "Managed file diverged from the last generated hash.", currentHash, proposedHash, true, hasDiff);
    }
}
