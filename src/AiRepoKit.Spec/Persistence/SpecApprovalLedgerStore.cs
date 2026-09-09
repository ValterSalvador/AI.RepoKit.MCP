using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AiRepoKit.Spec.Persistence;

public sealed class SpecApprovalLedgerStore
{
    private static readonly UTF8Encoding _strictUtf8 =
        new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    private readonly string _repositoryRoot;

    private readonly SpecId _specId;

    private readonly string _specDirectory;

    public SpecApprovalLedgerStore(
        string repositoryRoot_,
        SpecId specId_)
    {
        try
        {
            SpecArtifactPaths paths =
                new(
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
                "The canonical spec approval ledger path could not be inspected safely.",
                innerException_: exception);
        }
    }

    public string RepositoryRoot =>
        this._repositoryRoot;

    public SpecId SpecId =>
        this._specId;

    public string SpecDirectory =>
        this._specDirectory;

    public SpecApprovalLedger? Load()
    {
        string ledgerPath;
        try
        {
            SpecArtifactPaths paths =
                new(
                    this._repositoryRoot,
                    this._specId);
            ledgerPath =
                paths.GetApprovalLedgerPath();

            if (Directory.Exists(
                    ledgerPath))
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.ReadFailed,
                    "The canonical spec approval ledger path is occupied by a directory.");
            }

            if (!File.Exists(
                    ledgerPath))
            {
                return null;
            }
        }
        catch (SpecPersistenceException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            InvalidOperationException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ReadFailed,
                "The canonical spec approval ledger path could not be inspected safely.",
                innerException_: exception);
        }

        byte[] bytes;
        try
        {
            using FileStream stream =
                new(
                    ledgerPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);

            if (stream.Length >
                SpecWorkspace.MaximumArtifactSizeBytes)
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.ArtifactTooLarge,
                    $"The canonical approval ledger exceeds the {SpecWorkspace.MaximumArtifactSizeBytes}-byte limit.");
            }

            bytes =
                ReadBounded(
                    stream);
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
                "The canonical approval ledger could not be read.",
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
                "The canonical approval ledger is not valid UTF-8.",
                innerException_: exception);
        }

        SpecApprovalLedger ledger;
        try
        {
            ledger =
                SpecJsonSerializer.Deserialize<SpecApprovalLedger>(
                    json);
        }
        catch (Exception exception) when (
            exception is JsonException or
            NotSupportedException or
            ArgumentException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.InvalidJson,
                "The canonical approval ledger is not valid spec JSON.",
                innerException_: exception);
        }

        IReadOnlyList<SpecValidationError> validationErrors =
            SpecApprovalLedgerValidator.Validate(
                ledger,
                this._specId);

        if (validationErrors.Count > 0)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                "The canonical approval ledger is invalid.",
                validationErrors_: validationErrors);
        }

        return ledger;
    }

    public SpecApprovalLedgerStoreResult Append(
        Func<StableEntityId, Approval> approvalFactory_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            approvalFactory_);
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
            () =>
                this.AppendCoordinated(
                    approvalFactory_,
                    options_));
    }

    private SpecApprovalLedgerStoreResult AppendCoordinated(
        Func<StableEntityId, Approval> approvalFactory_,
        SpecStoreOptions options_)
    {
        SpecApprovalLedger? currentLedger =
            this.Load();

        StableEntityId allocatedId =
            AllocateNextApprovalId(
                currentLedger);

        Approval candidateApproval;
        try
        {
            candidateApproval =
                approvalFactory_(
                    allocatedId);
        }
        catch (Exception exception) when (
            exception is not SpecPersistenceException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                "The approval factory threw an exception.",
                innerException_: exception);
        }

        if (candidateApproval is null)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                "The approval factory returned a null approval.");
        }

        if (!string.Equals(
                candidateApproval.Id.Value,
                allocatedId.Value,
                StringComparison.Ordinal))
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                $"The approval factory returned ID '{candidateApproval.Id.Value}', but ID '{allocatedId.Value}' was allocated.");
        }

        IReadOnlyList<SpecValidationError> candidateErrors =
            ApprovalValidator.Validate(
                candidateApproval);

        if (candidateErrors.Count > 0)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                "The approval returned by the factory is invalid.",
                validationErrors_: candidateErrors);
        }

        string computedDigest =
            SpecSemanticDigest.ComputeFromCanonicalRepresentation(
                candidateApproval.CanonicalSemanticRepresentation);

        if (!string.Equals(
                candidateApproval.SemanticDigest,
                computedDigest,
                StringComparison.Ordinal))
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                $"Approval '{candidateApproval.Id.Value}' semantic digest does not match its canonical semantic representation.",
                validationErrors_:
                [
                    new SpecValidationError
                    {
                        Code =
                            SpecApprovalLedgerValidationErrorCodes.InconsistentSemanticDigest,
                        SourceEntityId =
                            candidateApproval.Id.Value,
                        Message =
                            $"Approval '{candidateApproval.Id.Value}' semantic digest does not match its canonical semantic representation."
                    }
                ]);
        }

        // Exact semantic reapproval idempotency check
        Approval? existingExactMatch =
            null;

        if (currentLedger?.Approvals is not null)
        {
            foreach (Approval existing in
                     currentLedger.Approvals)
            {
                if (existing is null)
                {
                    continue;
                }

                if (existing.ArtifactKind ==
                    candidateApproval.ArtifactKind &&
                    string.Equals(
                        existing.ArtifactIdentity,
                        candidateApproval.ArtifactIdentity,
                        StringComparison.Ordinal) &&
                    existing.ArtifactRevision ==
                    candidateApproval.ArtifactRevision &&
                    string.Equals(
                        existing.CanonicalizationId,
                        candidateApproval.CanonicalizationId,
                        StringComparison.Ordinal) &&
                    existing.CanonicalizationVersion ==
                    candidateApproval.CanonicalizationVersion &&
                    string.Equals(
                        existing.DigestAlgorithm,
                        candidateApproval.DigestAlgorithm,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        existing.CanonicalSemanticRepresentation,
                        candidateApproval.CanonicalSemanticRepresentation,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        existing.SemanticDigest,
                        candidateApproval.SemanticDigest,
                        StringComparison.Ordinal))
                {
                    existingExactMatch =
                        existing;
                    break;
                }
            }
        }

        if (existingExactMatch is not null)
        {
            return new SpecApprovalLedgerStoreResult(
                mode_: options_.Mode,
                changed_: false,
                applied_: false,
                previousRevision_: currentLedger!.Revision,
                targetRevision_: currentLedger.Revision,
                approval_: existingExactMatch);
        }

        // Artifact-revision integrity conflict check
        if (currentLedger?.Approvals is not null)
        {
            foreach (Approval existing in
                     currentLedger.Approvals)
            {
                if (existing is null)
                {
                    continue;
                }

                if (existing.ArtifactKind ==
                    candidateApproval.ArtifactKind &&
                    string.Equals(
                        existing.ArtifactIdentity,
                        candidateApproval.ArtifactIdentity,
                        StringComparison.Ordinal) &&
                    existing.ArtifactRevision ==
                    candidateApproval.ArtifactRevision)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.ValidationFailed,
                        $"An approval already exists for {candidateApproval.ArtifactKind} revision {candidateApproval.ArtifactRevision.Value} with different semantic content.",
                        validationErrors_:
                        [
                            new SpecValidationError
                            {
                                Code =
                                    SpecApprovalLedgerValidationErrorCodes.ConflictingApprovalBinding,
                                SourceEntityId =
                                    candidateApproval.Id.Value,
                                TargetEntityId =
                                    existing.Id.Value,
                                Message =
                                    $"Conflicting approval binding found between approval '{candidateApproval.Id.Value}' and existing approval '{existing.Id.Value}' for artifact '{candidateApproval.ArtifactKind}' revision '{candidateApproval.ArtifactRevision.Value}'."
                            }
                        ]);
                }
            }
        }

        // Expected ledger revision check
        ArtifactRevision? currentRevision =
            currentLedger?.Revision;

        if (currentRevision !=
            options_.ExpectedCurrentRevision)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.RevisionConflict,
                "The canonical approval ledger revision does not match the expected current revision.");
        }

        // Target revision calculation
        ArtifactRevision targetRevision;
        if (currentRevision is null)
        {
            targetRevision =
                new ArtifactRevision(1);
        }
        else
        {
            try
            {
                targetRevision =
                    new ArtifactRevision(
                        checked(currentRevision.Value.Value + 1));
            }
            catch (OverflowException exception)
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.WriteFailed,
                    "The canonical approval ledger revision cannot be incremented due to overflow.",
                    innerException_: exception);
            }
        }

        // Target ledger construction
        List<Approval> targetApprovals =
            currentLedger is not null
                ? [.. currentLedger.Approvals, candidateApproval]
                : [candidateApproval];

        SpecApprovalLedger targetLedger =
            new()
            {
                SchemaId =
                    SpecSchema.SchemaId,
                SchemaVersion =
                    SpecSchema.SchemaVersion,
                SpecId =
                    this._specId.Value,
                ArtifactIdentity =
                    SpecApprovalLedger.DefaultArtifactIdentity,
                Revision =
                    targetRevision,
                Approvals =
                    targetApprovals
            };

        // Target ledger validation
        IReadOnlyList<SpecValidationError> targetValidationErrors =
            SpecApprovalLedgerValidator.Validate(
                targetLedger,
                this._specId);

        if (targetValidationErrors.Count > 0)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                "The target approval ledger is invalid.",
                validationErrors_: targetValidationErrors);
        }

        // Serialization and size check
        byte[] payload;
        try
        {
            payload =
                _strictUtf8.GetBytes(
                    SpecJsonSerializer.Serialize(
                        targetLedger));
        }
        catch (Exception exception) when (
            exception is JsonException or
            NotSupportedException or
            ArgumentException or
            InvalidOperationException)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ValidationFailed,
                "The target approval ledger could not be serialized as spec JSON.",
                innerException_: exception);
        }

        if (payload.Length >
            SpecWorkspace.MaximumArtifactSizeBytes)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ArtifactTooLarge,
                $"The prospective canonical approval ledger exceeds the {SpecWorkspace.MaximumArtifactSizeBytes}-byte limit.");
        }

        if (options_.Mode ==
            SpecWriteMode.DryRun)
        {
            return new SpecApprovalLedgerStoreResult(
                mode_: options_.Mode,
                changed_: true,
                applied_: false,
                previousRevision_: currentRevision,
                targetRevision_: targetRevision,
                approval_: candidateApproval);
        }

        string ledgerPath =
            this.EnsureCanonicalDirectories();

        SpecAtomicFileWriter.Write(
            ledgerPath,
            payload,
            () =>
            {
                SpecArtifactPaths verifyPaths =
                    new(
                        this._repositoryRoot,
                        this._specId);
                _ =
                    verifyPaths.GetApprovalLedgerPath();
            });

        return new SpecApprovalLedgerStoreResult(
            mode_: options_.Mode,
            changed_: true,
            applied_: true,
            previousRevision_: currentRevision,
            targetRevision_: targetRevision,
            approval_: candidateApproval);
    }

    private string EnsureCanonicalDirectories()
    {
        if (!Directory.Exists(
                this._repositoryRoot))
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.WriteFailed,
                "The supplied repository root must already exist before applying a spec write.");
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

            SpecArtifactPaths paths =
                new(
                    this._repositoryRoot,
                    this._specId);
            string ledgerPath =
                paths.GetApprovalLedgerPath();

            if (Directory.Exists(
                    ledgerPath))
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.WriteFailed,
                    "The canonical spec approval ledger path is occupied by a directory.");
            }

            return ledgerPath;
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
                innerException_: exception);
        }
    }

    private static StableEntityId AllocateNextApprovalId(
        SpecApprovalLedger? ledger_)
    {
        int maxSuffix =
            0;

        if (ledger_?.Approvals is not null)
        {
            foreach (Approval approval in
                     ledger_.Approvals)
            {
                if (approval is null)
                {
                    continue;
                }

                string idValue =
                    approval.Id.Value;

                if (idValue.StartsWith(
                        "APR-",
                        StringComparison.Ordinal))
                {
                    string suffixString =
                        idValue[4..];

                    if (int.TryParse(
                            suffixString,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int suffixValue) &&
                        suffixValue > 0)
                    {
                        if (suffixValue >
                            maxSuffix)
                        {
                            maxSuffix =
                                suffixValue;
                        }
                    }
                }
            }
        }

        int nextNumber;
        try
        {
            nextNumber =
                checked(maxSuffix + 1);
        }
        catch (OverflowException exception)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.WriteFailed,
                "The next approval ID cannot be allocated due to numeric overflow.",
                innerException_: exception);
        }

        string nextIdString =
            nextNumber < 1000
                ? $"APR-{nextNumber:D3}"
                : $"APR-{nextNumber}";

        if (!StableEntityId.TryParse(
                nextIdString,
                out StableEntityId nextId))
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.WriteFailed,
                $"The next approval ID '{nextIdString}' is not a valid stable entity ID.");
        }

        return nextId;
    }

    private static byte[] ReadBounded(
        Stream stream_)
    {
        byte[] buffer =
            new byte[SpecWorkspace.MaximumArtifactSizeBytes + 1];
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
            SpecWorkspace.MaximumArtifactSizeBytes)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.ArtifactTooLarge,
                $"The canonical approval ledger exceeds the {SpecWorkspace.MaximumArtifactSizeBytes}-byte limit.");
        }

        return buffer[..totalRead];
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
}
