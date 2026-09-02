namespace AiRepoKit.Spec.Persistence;

public sealed class SpecArtifactPaths
{
    public SpecArtifactPaths(
        string repositoryRoot_,
        SpecId specId_)
    {
        this.RepositoryRoot =
            Path.GetFullPath(
                repositoryRoot_);
        this.SpecId =
            specId_;

        if (!SpecId.IsValid(
                specId_.Value))
        {
            throw new ArgumentException(
                "A valid spec ID is required.",
                nameof(specId_));
        }

        this.SpecDirectory =
            this.GetContainedPath(
                ".ai",
                "specs",
                specId_.Value);

        this.RejectExistingReparsePoints(
            Path.Combine(
                this.RepositoryRoot,
                ".ai"),
            Path.Combine(
                this.RepositoryRoot,
                ".ai",
                "specs"),
            this.SpecDirectory);
    }

    public string RepositoryRoot { get; }

    public SpecId SpecId { get; }

    public string SpecDirectory { get; }

    public string GetArtifactPath(
        SpecArtifactKind artifactKind_)
    {
        string fileName =
            artifactKind_ switch
            {
                SpecArtifactKind.RequirementSet =>
                    "requirements.json",
                SpecArtifactKind.WorkSpec =>
                    "work-spec.json",
                SpecArtifactKind.ImplementationPlan =>
                    "implementation-plan.json",
                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(artifactKind_),
                        artifactKind_,
                        "Unsupported spec artifact kind.")
            };

        string artifactPath =
            this.GetContainedPath(
                ".ai",
                "specs",
                this.SpecId.Value,
                fileName);

        this.RejectExistingReparsePoints(
            artifactPath);

        return artifactPath;
    }

    private string GetContainedPath(
        params string[] segments_)
    {
        string candidate =
            Path.GetFullPath(
                Path.Combine(
                    [this.RepositoryRoot, .. segments_]));
        string relativePath =
            Path.GetRelativePath(
                this.RepositoryRoot,
                candidate);
        StringComparison comparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (Path.IsPathRooted(
                relativePath) ||
            relativePath.Equals(
                "..",
                comparison) ||
            relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                comparison) ||
            relativePath.StartsWith(
                $"..{Path.AltDirectorySeparatorChar}",
                comparison))
        {
            throw new InvalidOperationException(
                "The spec artifact path escapes the supplied repository root.");
        }

        return candidate;
    }

    private void RejectExistingReparsePoints(
        params string[] paths_)
    {
        foreach (string path in paths_)
        {
            try
            {
                FileAttributes attributes =
                    File.GetAttributes(
                        path);

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Spec artifact path component is a symbolic link or reparse point: {path}");
                }
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}
