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

    private readonly string _specDirectory;

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
            this._specDirectory =
                paths.SpecDirectory;
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

    public SpecStoreResult Store(
        RequirementSet requirementSet_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        return this.Store(
            SpecArtifactKind.RequirementSet,
            requirementSet_,
            options_,
            snapshot_ =>
                snapshot_.RequirementSet,
            (candidate_, _) =>
                SpecWorkspaceValidator.ValidateForStore(
                    candidate_),
            (artifact_, revision_) =>
                artifact_ with
                {
                    Revision =
                        revision_
                },
            SpecSemanticDigest.Compute);
    }

    public SpecStoreResult Store(
        WorkSpec workSpec_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            workSpec_);

        return this.Store(
            SpecArtifactKind.WorkSpec,
            workSpec_,
            options_,
            snapshot_ =>
                snapshot_.WorkSpec,
            (candidate_, snapshot_) =>
            {
                RequirementSet requirementSet =
                    snapshot_.RequirementSet ??
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "A WorkSpec cannot be stored without a canonical RequirementSet.",
                        SpecArtifactKind.WorkSpec);

                SpecWorkspaceValidator.ValidateForStore(
                    candidate_,
                    requirementSet);
            },
            (artifact_, revision_) =>
                artifact_ with
                {
                    Revision =
                        revision_
                },
            SpecSemanticDigest.Compute);
    }

    public SpecStoreResult Store(
        ImplementationPlan implementationPlan_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        return this.Store(
            SpecArtifactKind.ImplementationPlan,
            implementationPlan_,
            options_,
            snapshot_ =>
                snapshot_.ImplementationPlan,
            (candidate_, snapshot_) =>
            {
                if (snapshot_.RequirementSet is null ||
                    snapshot_.WorkSpec is null)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "An ImplementationPlan cannot be stored without canonical RequirementSet and WorkSpec dependencies.",
                        SpecArtifactKind.ImplementationPlan);
                }

                if (snapshot_.IsWorkSpecStale)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.StaleDependency,
                        "An ImplementationPlan cannot be stored against a stale canonical WorkSpec.",
                        SpecArtifactKind.ImplementationPlan);
                }

                SpecWorkspaceValidator.ValidateForStore(
                    candidate_,
                    snapshot_.WorkSpec,
                    snapshot_.RequirementSet);
            },
            (artifact_, revision_) =>
                artifact_ with
                {
                    Revision =
                        revision_
                },
            SpecSemanticDigest.Compute);
    }

    private SpecStoreResult Store<T>(
        SpecArtifactKind artifactKind_,
        T candidate_,
        SpecStoreOptions options_,
        Func<SpecWorkspaceSnapshot, T?> getCurrent_,
        Action<T, SpecWorkspaceSnapshot> validate_,
        Func<T, ArtifactRevision, T> withRevision_,
        Func<T, string> computeDigest_)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(
            options_);

        if (options_.Mode is not
            (SpecWriteMode.DryRun or SpecWriteMode.Apply))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options_),
                options_.Mode,
                "Unsupported spec write mode.");
        }

        return SpecWorkspaceWriteCoordinator.Execute(
            this._specDirectory,
            artifactKind_,
            () =>
                this.StoreCoordinated(
                    artifactKind_,
                    candidate_,
                    options_,
                    getCurrent_,
                    validate_,
                    withRevision_,
                    computeDigest_));
    }

    private SpecStoreResult StoreCoordinated<T>(
        SpecArtifactKind artifactKind_,
        T candidate_,
        SpecStoreOptions options_,
        Func<SpecWorkspaceSnapshot, T?> getCurrent_,
        Action<T, SpecWorkspaceSnapshot> validate_,
        Func<T, ArtifactRevision, T> withRevision_,
        Func<T, string> computeDigest_)
        where T : class
    {
        SpecWorkspaceSnapshot snapshot =
            this.Load();
        T? current =
            getCurrent_(
                snapshot);
        ArtifactRevision? previousRevision =
            GetRevision(
                current);
        ArtifactRevision planningRevision =
            previousRevision ??
            new ArtifactRevision(
                1);
        T normalizedCandidate =
            withRevision_(
                candidate_,
                planningRevision);

        string? semanticDigest =
            null;
        string? currentSemanticDigest =
            null;
        bool semanticProbeCompleted =
            current is not null &&
            TryComputeSemanticDigests(
                normalizedCandidate,
                current,
                computeDigest_,
                out semanticDigest,
                out currentSemanticDigest);

        if (semanticProbeCompleted &&
            string.Equals(
                currentSemanticDigest,
                semanticDigest,
                StringComparison.Ordinal))
        {
            return new SpecStoreResult(
                artifactKind_,
                options_.Mode,
                changed_: false,
                applied_: false,
                previousRevision,
                previousRevision!.Value,
                semanticDigest!);
        }

        validate_(
            normalizedCandidate,
            snapshot);

        string changedSemanticDigest =
            semanticDigest ??
            computeDigest_(
                normalizedCandidate);

        if (current is not null &&
            !semanticProbeCompleted &&
            string.Equals(
                computeDigest_(
                    current),
                changedSemanticDigest,
                StringComparison.Ordinal))
        {
            return new SpecStoreResult(
                artifactKind_,
                options_.Mode,
                changed_: false,
                applied_: false,
                previousRevision,
                previousRevision!.Value,
                changedSemanticDigest);
        }

        this.ValidateExpectedRevision(
            artifactKind_,
            previousRevision,
            options_.ExpectedCurrentRevision);

        ArtifactRevision targetRevision =
            previousRevision is null
                ? new ArtifactRevision(
                    1)
                : IncrementRevision(
                    artifactKind_,
                    previousRevision.Value);
        T targetArtifact =
            withRevision_(
                candidate_,
                targetRevision);

        validate_(
            targetArtifact,
            snapshot);

        byte[] payload =
            this.Serialize(
                artifactKind_,
                targetArtifact);

        if (options_.Mode ==
            SpecWriteMode.DryRun)
        {
            return new SpecStoreResult(
                artifactKind_,
                options_.Mode,
                changed_: true,
                applied_: false,
                previousRevision,
                targetRevision,
                changedSemanticDigest);
        }

        string artifactPath =
            this.EnsureCanonicalDirectories(
                artifactKind_);

        SpecAtomicFileWriter.Write(
            artifactPath,
            payload,
            () =>
                this.RevalidateArtifactPath(
                    artifactKind_));

        return new SpecStoreResult(
            artifactKind_,
            options_.Mode,
            changed_: true,
            applied_: true,
            previousRevision,
            targetRevision,
            changedSemanticDigest);
    }

    private static bool TryComputeSemanticDigests<T>(
        T candidate_,
        T current_,
        Func<T, string> computeDigest_,
        out string? candidateDigest_,
        out string? currentDigest_)
    {
        try
        {
            candidateDigest_ =
                computeDigest_(
                    candidate_);
            currentDigest_ =
                computeDigest_(
                    current_);

            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            InvalidOperationException or
            NullReferenceException)
        {
            candidateDigest_ =
                null;
            currentDigest_ =
                null;

            return false;
        }
    }

    private byte[] Serialize<T>(
        SpecArtifactKind artifactKind_,
        T artifact_)
    {
        byte[] payload;

        try
        {
            payload =
                _strictUtf8.GetBytes(
                    SpecJsonSerializer.Serialize(
                        artifact_));
        }
        catch (Exception exception) when (
            exception is JsonException or
            NotSupportedException or
            ArgumentException or
            InvalidOperationException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                $"The canonical {artifactKind_} could not be serialized as spec JSON.",
                artifactKind_,
                innerException_: exception);
        }

        if (payload.Length >
            MaximumArtifactSizeBytes)
        {
            throw CreateArtifactTooLargeException(
                artifactKind_);
        }

        return payload;
    }

    private string EnsureCanonicalDirectories(
        SpecArtifactKind artifactKind_)
    {
        if (!Directory.Exists(
                this._repositoryRoot))
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.WriteFailed,
                "The supplied repository root must already exist before applying a spec write.",
                artifactKind_);
        }

        string[] directories =
        [
            Path.Combine(
                this._repositoryRoot,
                ".ai"),
            Path.Combine(
                this._repositoryRoot,
                ".ai",
                "specs"),
            this._specDirectory
        ];

        try
        {
            foreach (string directory in
                     directories)
            {
                _ =
                    new SpecArtifactPaths(
                        this._repositoryRoot,
                        this._specId);

                if (File.Exists(
                        directory))
                {
                    throw new IOException(
                        "A canonical spec directory path is occupied by a file.");
                }

                Directory.CreateDirectory(
                    directory);

                _ =
                    new SpecArtifactPaths(
                        this._repositoryRoot,
                        this._specId);
            }

            return this.RevalidateArtifactPath(
                artifactKind_);
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
                SpecPersistenceException.WriteFailed,
                "The canonical spec workspace directories could not be created safely.",
                artifactKind_,
                innerException_: exception);
        }
    }

    private string RevalidateArtifactPath(
        SpecArtifactKind artifactKind_)
    {
        SpecArtifactPaths paths =
            new(
                this._repositoryRoot,
                this._specId);

        return paths.GetArtifactPath(
            artifactKind_);
    }

    private void ValidateExpectedRevision(
        SpecArtifactKind artifactKind_,
        ArtifactRevision? currentRevision_,
        ArtifactRevision? expectedRevision_)
    {
        if (currentRevision_ ==
            expectedRevision_)
        {
            return;
        }

        throw new SpecPersistenceException(
            SpecPersistenceException.RevisionConflict,
            "The canonical artifact revision does not match the expected current revision.",
            artifactKind_);
    }

    private static ArtifactRevision IncrementRevision(
        SpecArtifactKind artifactKind_,
        ArtifactRevision currentRevision_)
    {
        try
        {
            return new ArtifactRevision(
                checked(
                    currentRevision_.Value + 1));
        }
        catch (OverflowException exception)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.WriteFailed,
                "The canonical artifact revision cannot be incremented.",
                artifactKind_,
                innerException_: exception);
        }
    }

    private static ArtifactRevision? GetRevision<T>(
        T? artifact_)
        where T : class
    {
        return artifact_ switch
        {
            RequirementSet requirementSet =>
                requirementSet.Revision,
            WorkSpec workSpec =>
                workSpec.Revision,
            ImplementationPlan implementationPlan =>
                implementationPlan.Revision,
            null =>
                null,
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(artifact_))
        };
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
