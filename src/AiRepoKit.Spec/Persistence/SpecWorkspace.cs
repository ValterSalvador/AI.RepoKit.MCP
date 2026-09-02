using System.Text;
using System.Text.Json;

namespace AiRepoKit.Spec.Persistence;

public sealed class SpecWorkspace
{
    public const int MaximumArtifactSizeBytes =
        1_048_576;

    private static readonly UTF8Encoding _strictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    private readonly string _repositoryRoot;

    private readonly SpecId _specId;

    public SpecWorkspace(
        string repositoryRoot_,
        SpecId specId_)
    {
        try
        {
            SpecArtifactPaths paths =
                new SpecArtifactPaths(
                    repositoryRoot_,
                    specId_);

            this._repositoryRoot =
                paths.RepositoryRoot;
            this._specId =
                paths.SpecId;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ReadFailed,
                "The canonical spec workspace path could not be inspected safely.",
                innerException_: exception);
        }
    }

    public SpecWorkspaceSnapshot Load()
    {
        ArtifactFile requirementSetFile =
            this.InspectArtifact(
                SpecArtifactKind.RequirementSet);
        ArtifactFile workSpecFile =
            this.InspectArtifact(
                SpecArtifactKind.WorkSpec);
        ArtifactFile implementationPlanFile =
            this.InspectArtifact(
                SpecArtifactKind.ImplementationPlan);

        SpecWorkspaceValidator.ValidateDependencyPresence(
            requirementSetFile.Exists,
            workSpecFile.Exists,
            implementationPlanFile.Exists);

        RequirementSet? requirementSet =
            requirementSetFile.Exists
                ? this.ReadArtifact<RequirementSet>(
                    requirementSetFile)
                : null;
        WorkSpec? workSpec =
            workSpecFile.Exists
                ? this.ReadArtifact<WorkSpec>(
                    workSpecFile)
                : null;
        ImplementationPlan? implementationPlan =
            implementationPlanFile.Exists
                ? this.ReadArtifact<ImplementationPlan>(
                    implementationPlanFile)
                : null;

        return SpecWorkspaceValidator.Validate(
            requirementSet,
            workSpec,
            implementationPlan);
    }

    private ArtifactFile InspectArtifact(
        SpecArtifactKind artifactKind_)
    {
        try
        {
            string path =
                this.GetArtifactPath(
                    artifactKind_);

            return new ArtifactFile(
                artifactKind_,
                File.Exists(
                    path) ||
                Directory.Exists(
                    path));
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ReadFailed,
                $"The canonical {artifactKind_} path could not be inspected safely.",
                artifactKind_,
                innerException_: exception);
        }
    }

    private T ReadArtifact<T>(
        ArtifactFile artifactFile_)
    {
        byte[] bytes;

        try
        {
            string artifactPath =
                this.GetArtifactPath(
                    artifactFile_.Kind);

            using FileStream stream =
                new(
                    artifactPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);

            if (stream.Length >
                MaximumArtifactSizeBytes)
            {
                throw CreateArtifactTooLargeException(
                    artifactFile_.Kind);
            }

            bytes =
                ReadBounded(
                    stream,
                    artifactFile_.Kind);
        }
        catch (SpecPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            NotSupportedException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ReadFailed,
                $"The canonical {artifactFile_.Kind} could not be read.",
                artifactFile_.Kind,
                innerException_: exception);
        }

        string json;

        try
        {
            int offset =
                HasUtf8Bom(
                    bytes)
                    ? 3
                    : 0;

            json =
                _strictUtf8.GetString(
                    bytes,
                    offset,
                    bytes.Length - offset);
        }
        catch (DecoderFallbackException exception)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.InvalidUtf8,
                $"The canonical {artifactFile_.Kind} is not valid UTF-8.",
                artifactFile_.Kind,
                innerException_: exception);
        }

        try
        {
            return SpecJsonSerializer.Deserialize<T>(
                json);
        }
        catch (Exception exception) when (
            exception is JsonException or
            NotSupportedException or
            ArgumentException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.InvalidJson,
                $"The canonical {artifactFile_.Kind} is not valid spec JSON.",
                artifactFile_.Kind);
        }
    }

    private string GetArtifactPath(
        SpecArtifactKind artifactKind_)
    {
        SpecArtifactPaths paths =
            new(
                this._repositoryRoot,
                this._specId);

        return paths.GetArtifactPath(
            artifactKind_);
    }

    private static byte[] ReadBounded(
        Stream stream_,
        SpecArtifactKind artifactKind_)
    {
        byte[] buffer =
            new byte[MaximumArtifactSizeBytes + 1];
        int totalRead =
            0;

        while (totalRead <
               buffer.Length)
        {
            int read =
                stream_.Read(
                    buffer,
                    totalRead,
                    buffer.Length - totalRead);

            if (read == 0)
            {
                break;
            }

            totalRead +=
                read;
        }

        if (totalRead >
            MaximumArtifactSizeBytes)
        {
            throw CreateArtifactTooLargeException(
                artifactKind_);
        }

        return buffer[..totalRead];
    }

    private static SpecPersistenceException CreateArtifactTooLargeException(
        SpecArtifactKind artifactKind_)
    {
        return new SpecPersistenceException(
            SpecPersistenceException.ArtifactTooLarge,
            $"The canonical {artifactKind_} exceeds the 1048576-byte limit.",
            artifactKind_);
    }

    private static bool HasUtf8Bom(
        byte[] bytes_)
    {
        return
            bytes_.Length >= 3 &&
            bytes_[0] == 0xEF &&
            bytes_[1] == 0xBB &&
            bytes_[2] == 0xBF;
    }

    private sealed record ArtifactFile(
        SpecArtifactKind Kind,
        bool Exists);
}
