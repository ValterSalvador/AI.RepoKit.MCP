using System.Text;
using AiRepoKit.Cli.McpRuntime.Models;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Cli.McpRuntime.Services;

public sealed class SpecMcpContextReader
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly string _repoRoot;
    private readonly ContextBudget _budget;

    public SpecMcpContextReader(string repoRoot_, ContextBudget budget_)
    {
        this._repoRoot = Path.GetFullPath(repoRoot_ ?? throw new ArgumentNullException(nameof(repoRoot_)));
        this._budget = budget_ ?? throw new ArgumentNullException(nameof(budget_));
    }

    public object ReadSpec(ContextDetail detail_, int? limit_, string? target_)
    {
        if (!this.ValidateTarget(target_, out SpecId specId, out ToolError? error))
        {
            return error!;
        }

        string specDir = Path.Combine(this._repoRoot, ".ai", "specs", specId.Value);
        if (HasReparsePointInChain(this._repoRoot, specDir))
        {
            return ToolError.Create(
                "SPEC_CONTEXT_PATH_REJECTED",
                "Spec path contains a symbolic link or reparse point.",
                string.Empty,
                true,
                new { specId = specId.Value });
        }

        SpecWorkspace workspace = new(this._repoRoot, specId);
        SpecApprovalLedgerStore ledgerStore = new(this._repoRoot, specId);
        SpecWorkspaceSnapshot snapshot;
        try
        {
            snapshot = workspace.Load();
        }
        catch (SpecPersistenceException ex) when (ex.ErrorCode == SpecPersistenceException.MissingDependency || ex.ErrorCode == SpecPersistenceException.ReadFailed)
        {
            return ToolError.Create(
                "SPEC_NOT_FOUND",
                $"The canonical spec '{specId.Value}' was not found or has invalid dependencies.",
                string.Empty,
                true,
                new { specId = specId.Value });
        }

        if (snapshot.IsEmpty)
        {
            return ToolError.Create(
                "SPEC_NOT_FOUND",
                $"The canonical spec '{specId.Value}' was not found or contains no canonical artifacts.",
                $"airepo spec init {specId.Value} --apply",
                true,
                new { specId = specId.Value });
        }

        SpecApprovalLedger? ledger = null;
        try
        {
            ledger = ledgerStore.Load();
        }
        catch
        {
        }

        IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses = SpecApprovalStatusEvaluator.Evaluate(snapshot, ledger);
        SpecApprovalStatus reqApproval = approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.RequirementSet)?.Status ?? SpecApprovalStatus.NotApproved;
        SpecApprovalStatus wsApproval = approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.WorkSpec)?.Status ?? SpecApprovalStatus.NotApproved;
        SpecApprovalStatus planApproval = approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan)?.Status ?? SpecApprovalStatus.NotApproved;

        string? reqDigest = snapshot.RequirementSet is not null ? SpecSemanticDigest.Compute(snapshot.RequirementSet) : null;
        string? wsDigest = snapshot.WorkSpec is not null ? SpecSemanticDigest.Compute(snapshot.WorkSpec) : null;
        string? planDigest = snapshot.ImplementationPlan is not null ? SpecSemanticDigest.Compute(snapshot.ImplementationPlan) : null;

        if (detail_ == ContextDetail.Brief)
        {
            return new
            {
                available = true,
                specId = specId.Value,
                requirementSet = new
                {
                    present = snapshot.RequirementSet is not null,
                    revision = snapshot.RequirementSet?.Revision.Value,
                    approvalStatus = FormatApprovalStatus(reqApproval),
                    semanticDigest = reqDigest,
                    requirementInputCount = snapshot.RequirementSet?.Inputs.Count ?? 0,
                    requirementCount = snapshot.RequirementSet?.Requirements.Count ?? 0
                },
                workSpec = new
                {
                    present = snapshot.WorkSpec is not null,
                    revision = snapshot.WorkSpec?.Revision.Value,
                    requirementSetRevision = snapshot.WorkSpec?.RequirementSetRevision.Value,
                    stale = snapshot.IsWorkSpecStale,
                    approvalStatus = FormatApprovalStatus(wsApproval),
                    semanticDigest = wsDigest,
                    constraintCount = snapshot.WorkSpec?.Constraints.Count ?? 0,
                    acceptanceCriterionCount = snapshot.WorkSpec?.AcceptanceCriteria.Count ?? 0
                },
                implementationPlan = new
                {
                    present = snapshot.ImplementationPlan is not null,
                    revision = snapshot.ImplementationPlan?.Revision.Value,
                    workSpecRevision = snapshot.ImplementationPlan?.WorkSpecRevision.Value,
                    stale = snapshot.IsImplementationPlanStale,
                    approvalStatus = FormatApprovalStatus(planApproval),
                    semanticDigest = planDigest,
                    stepCount = snapshot.ImplementationPlan?.Steps.Count ?? 0
                }
            };
        }

        int limit = Math.Clamp(limit_ ?? this._budget.Options.ArrayDefaultLimit, 1, this._budget.Options.ArrayHardLimit);

        object[] reqInputs = snapshot.RequirementSet?.Inputs
            .OrderBy(i_ => i_.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .Select(i_ => (object)new { id = i_.Id.Value, text = i_.Text })
            .ToArray() ?? [];

        object[] requirements = snapshot.RequirementSet?.Requirements
            .OrderBy(r_ => r_.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .Select(r_ => (object)new
            {
                id = r_.Id.Value,
                statement = r_.Statement,
                sourceInputIds = r_.SourceInputIds.Select(id_ => id_.Value).Order(StringComparer.Ordinal).Take(limit).ToArray()
            })
            .ToArray() ?? [];

        object[] constraints = snapshot.WorkSpec?.Constraints
            .OrderBy(c_ => c_.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .Select(c_ => (object)new
            {
                id = c_.Id.Value,
                statement = c_.Statement,
                requirementIds = c_.RequirementIds.Select(id_ => id_.Value).Order(StringComparer.Ordinal).Take(limit).ToArray()
            })
            .ToArray() ?? [];

        object[] acceptanceCriteria = snapshot.WorkSpec?.AcceptanceCriteria
            .OrderBy(a_ => a_.Id.Value, StringComparer.Ordinal)
            .Take(limit)
            .Select(a_ => (object)new
            {
                id = a_.Id.Value,
                statement = a_.Statement,
                requirementIds = a_.RequirementIds.Select(id_ => id_.Value).Order(StringComparer.Ordinal).Take(limit).ToArray()
            })
            .ToArray() ?? [];

        // Steps must preserve canonical semantic order
        object[] steps = snapshot.ImplementationPlan?.Steps
            .Take(limit)
            .Select(s_ => (object)new
            {
                id = s_.Id.Value,
                statement = s_.Statement,
                requirementIds = s_.RequirementIds.Select(id_ => id_.Value).Order(StringComparer.Ordinal).Take(limit).ToArray(),
                acceptanceCriterionIds = s_.AcceptanceCriterionIds.Select(id_ => id_.Value).Order(StringComparer.Ordinal).Take(limit).ToArray()
            })
            .ToArray() ?? [];

        return new
        {
            available = true,
            specId = specId.Value,
            requirementSet = new
            {
                present = snapshot.RequirementSet is not null,
                revision = snapshot.RequirementSet?.Revision.Value,
                approvalStatus = FormatApprovalStatus(reqApproval),
                semanticDigest = reqDigest,
                requirementInputCount = snapshot.RequirementSet?.Inputs.Count ?? 0,
                requirementCount = snapshot.RequirementSet?.Requirements.Count ?? 0,
                requirementInputs = reqInputs,
                requirements
            },
            workSpec = new
            {
                present = snapshot.WorkSpec is not null,
                revision = snapshot.WorkSpec?.Revision.Value,
                requirementSetRevision = snapshot.WorkSpec?.RequirementSetRevision.Value,
                stale = snapshot.IsWorkSpecStale,
                approvalStatus = FormatApprovalStatus(wsApproval),
                semanticDigest = wsDigest,
                constraintCount = snapshot.WorkSpec?.Constraints.Count ?? 0,
                acceptanceCriterionCount = snapshot.WorkSpec?.AcceptanceCriteria.Count ?? 0,
                constraints,
                acceptanceCriteria
            },
            implementationPlan = new
            {
                present = snapshot.ImplementationPlan is not null,
                revision = snapshot.ImplementationPlan?.Revision.Value,
                workSpecRevision = snapshot.ImplementationPlan?.WorkSpecRevision.Value,
                stale = snapshot.IsImplementationPlanStale,
                approvalStatus = FormatApprovalStatus(planApproval),
                semanticDigest = planDigest,
                stepCount = snapshot.ImplementationPlan?.Steps.Count ?? 0,
                steps
            }
        };
    }

    public object ReadSpecContext(ContextDetail detail_, int? limit_, string? target_)
    {
        if (!this.ValidateTarget(target_, out SpecId specId, out ToolError? error))
        {
            return error!;
        }

        string specContextRelPath = Path.Combine(".ai", "generated", "spec-context", $"{specId.Value}.json");
        string specContextFullPath = Path.GetFullPath(Path.Combine(this._repoRoot, specContextRelPath));

        if (!IsPathContained(this._repoRoot, specContextFullPath))
        {
            return ToolError.Create(
                "SPEC_CONTEXT_PATH_REJECTED",
                "SpecContext path is outside repository root.",
                string.Empty,
                true,
                new { target = target_ });
        }

        if (HasReparsePointInChain(this._repoRoot, specContextFullPath))
        {
            return ToolError.Create(
                "SPEC_CONTEXT_PATH_REJECTED",
                "SpecContext path contains a symbolic link or reparse point.",
                string.Empty,
                true,
                new { target = target_ });
        }

        if (!File.Exists(specContextFullPath))
        {
            return ToolError.Create(
                "SPEC_CONTEXT_NOT_FOUND",
                $"Persisted SpecContext for '{specId.Value}' was not found.",
                $"airepo spec context {specId.Value} --apply",
                true,
                new { specId = specId.Value, artifact = specContextRelPath.Replace('\\', '/') });
        }

        FileInfo fileInfo = new(specContextFullPath);
        if (fileInfo.Length > SpecContextPersistenceService.MaximumSpecContextSizeBytes)
        {
            return ToolError.Create(
                "SPEC_CONTEXT_TOO_LARGE",
                $"SpecContext file size ({fileInfo.Length} bytes) exceeds 1 MiB limit.",
                string.Empty,
                true,
                new { specId = specId.Value, sizeBytes = fileInfo.Length, maxBytes = SpecContextPersistenceService.MaximumSpecContextSizeBytes });
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(specContextFullPath);
        }
        catch (Exception ex)
        {
            return ToolError.Create(
                "SPEC_CONTEXT_INVALID",
                "Could not read SpecContext file: " + ex.Message,
                string.Empty,
                true,
                new { specId = specId.Value });
        }

        string json;
        try
        {
            json = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return ToolError.Create(
                "SPEC_CONTEXT_INVALID",
                "SpecContext file contains invalid UTF-8.",
                string.Empty,
                true,
                new { specId = specId.Value });
        }

        SpecContext specContext;
        try
        {
            specContext = SpecJsonSerializer.Deserialize<SpecContext>(json);
        }
        catch
        {
            return ToolError.Create(
                "SPEC_CONTEXT_INVALID",
                "SpecContext payload is invalid JSON or does not conform to schema.",
                string.Empty,
                true,
                new { specId = specId.Value });
        }

        if (!string.Equals(specContext.SpecId, specId.Value, StringComparison.Ordinal))
        {
            return ToolError.Create(
                "SPEC_CONTEXT_INVALID",
                $"SpecContext payload SpecId '{specContext.SpecId}' does not match target '{specId.Value}'.",
                string.Empty,
                true,
                new { specId = specId.Value, payloadSpecId = specContext.SpecId });
        }

        IReadOnlyList<SpecValidationError> validationErrors = SpecContextValidator.Validate(specContext);
        if (validationErrors.Count > 0)
        {
            return ToolError.Create(
                "SPEC_CONTEXT_INVALID",
                "SpecContext payload failed validation.",
                string.Empty,
                true,
                new { specId = specId.Value, errorCount = validationErrors.Count });
        }

        bool isStale;
        try
        {
            SpecWorkspace workspace = new(this._repoRoot, specId);
            SpecWorkspaceSnapshot canonicalSnapshot = workspace.Load();
            isStale = canonicalSnapshot.RequirementSet is null
                || canonicalSnapshot.WorkSpec is null
                || specContext.RequirementSetRevision != canonicalSnapshot.RequirementSet.Revision
                || specContext.WorkSpecRevision != canonicalSnapshot.WorkSpec.Revision;
        }
        catch
        {
            isStale = true;
        }

        if (detail_ == ContextDetail.Brief)
        {
            return new
            {
                available = true,
                specId = specContext.SpecId,
                schemaId = specContext.SchemaId,
                schemaVersion = specContext.SchemaVersion,
                requirementSetRevision = specContext.RequirementSetRevision.Value,
                workSpecRevision = specContext.WorkSpecRevision.Value,
                stale = isStale,
                target = specContext.Target,
                referenceLimit = specContext.ReferenceLimit,
                budget = specContext.Budget,
                estimatedTokens = specContext.EstimatedTokens,
                truncated = specContext.Truncated,
                evidenceCount = specContext.Evidence.Count,
                referenceCount = specContext.References.Count,
                omissionCount = specContext.Omissions.Count
            };
        }

        int limit = Math.Clamp(limit_ ?? this._budget.Options.ArrayDefaultLimit, 1, this._budget.Options.ArrayHardLimit);

        return new
        {
            available = true,
            specId = specContext.SpecId,
            schemaId = specContext.SchemaId,
            schemaVersion = specContext.SchemaVersion,
            requirementSetRevision = specContext.RequirementSetRevision.Value,
            workSpecRevision = specContext.WorkSpecRevision.Value,
            stale = isStale,
            target = specContext.Target,
            referenceLimit = specContext.ReferenceLimit,
            budget = specContext.Budget,
            estimatedTokens = specContext.EstimatedTokens,
            truncated = specContext.Truncated,
            evidenceCount = specContext.Evidence.Count,
            referenceCount = specContext.References.Count,
            omissionCount = specContext.Omissions.Count,
            evidence = specContext.Evidence.Take(limit).ToArray(),
            references = specContext.References.Take(limit).ToArray(),
            omissions = specContext.Omissions.Take(limit).ToArray()
        };
    }

    public object ReadVerification(ContextDetail detail_, int? limit_, string? target_)
    {
        if (!this.ValidateTarget(target_, out SpecId specId, out ToolError? error))
        {
            return error!;
        }

        string specDir = Path.Combine(this._repoRoot, ".ai", "specs", specId.Value);
        if (HasReparsePointInChain(this._repoRoot, specDir))
        {
            return ToolError.Create(
                "SPEC_CONTEXT_PATH_REJECTED",
                "Spec path contains a symbolic link or reparse point.",
                string.Empty,
                true,
                new { specId = specId.Value });
        }

        SpecWorkspace workspace = new(this._repoRoot, specId);
        SpecApprovalLedgerStore ledgerStore = new(this._repoRoot, specId);
        SpecWorkspaceSnapshot? snapshot = null;
        try
        {
            snapshot = workspace.Load();
        }
        catch
        {
            // Snapshot remains null when workspace cannot be loaded
        }

        SpecApprovalLedger? ledger = null;
        try
        {
            ledger = ledgerStore.Load();
        }
        catch
        {
        }

        IReadOnlyList<SpecArtifactApprovalStatus> approvalStatuses = snapshot is not null
            ? SpecApprovalStatusEvaluator.Evaluate(snapshot, ledger)
            : [];
        SpecApprovalStatus reqApproval = approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.RequirementSet)?.Status ?? SpecApprovalStatus.NotApproved;
        SpecApprovalStatus wsApproval = approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.WorkSpec)?.Status ?? SpecApprovalStatus.NotApproved;
        SpecApprovalStatus planApproval = approvalStatuses.FirstOrDefault(s_ => s_.ArtifactKind == SpecArtifactKind.ImplementationPlan)?.Status ?? SpecApprovalStatus.NotApproved;

        List<string> blockers = [];
        if (snapshot?.RequirementSet is null)
        {
            blockers.Add("requirement-set-missing");
        }
        else if (reqApproval != SpecApprovalStatus.Current)
        {
            blockers.Add("requirement-set-not-current");
        }

        if (snapshot?.WorkSpec is null)
        {
            blockers.Add("work-spec-missing");
        }
        else
        {
            if (snapshot.IsWorkSpecStale)
            {
                blockers.Add("work-spec-stale");
            }
            if (wsApproval != SpecApprovalStatus.Current)
            {
                blockers.Add("work-spec-not-current");
            }
        }

        if (snapshot?.ImplementationPlan is null)
        {
            blockers.Add("implementation-plan-missing");
        }
        else
        {
            if (snapshot.IsImplementationPlanStale)
            {
                blockers.Add("implementation-plan-stale");
            }
            if (planApproval != SpecApprovalStatus.Current)
            {
                blockers.Add("implementation-plan-not-current");
            }
        }

        bool graphReady = blockers.Count == 0;

        bool specContextPresent = false;
        bool specContextStale = false;
        object[] evidence = [];

        string specContextRelPath = Path.Combine(".ai", "generated", "spec-context", $"{specId.Value}.json");
        string specContextFullPath = Path.GetFullPath(Path.Combine(this._repoRoot, specContextRelPath));

        if (IsPathContained(this._repoRoot, specContextFullPath)
            && !HasReparsePointInChain(this._repoRoot, specContextFullPath)
            && File.Exists(specContextFullPath))
        {
            try
            {
                FileInfo fileInfo = new(specContextFullPath);
                if (fileInfo.Length <= SpecContextPersistenceService.MaximumSpecContextSizeBytes)
                {
                    byte[] bytes = File.ReadAllBytes(specContextFullPath);
                    string json = StrictUtf8.GetString(bytes);
                    SpecContext specContext = SpecJsonSerializer.Deserialize<SpecContext>(json);
                    if (string.Equals(specContext.SpecId, specId.Value, StringComparison.Ordinal)
                        && SpecContextValidator.Validate(specContext).Count == 0)
                    {
                        specContextPresent = true;
                        specContextStale = snapshot is null
                            || snapshot.RequirementSet is null
                            || snapshot.WorkSpec is null
                            || specContext.RequirementSetRevision != snapshot.RequirementSet.Revision
                            || specContext.WorkSpecRevision != snapshot.WorkSpec.Revision;

                        int limit = Math.Clamp(limit_ ?? this._budget.Options.ArrayDefaultLimit, 1, this._budget.Options.ArrayHardLimit);
                        evidence = specContext.Evidence.Take(limit).Select(e_ => (object)new
                        {
                            evidenceId = e_.EvidenceId,
                            source = e_.Source,
                            kind = e_.Kind,
                            reference = e_.Reference,
                            availability = e_.Availability.ToString().ToLowerInvariant(),
                            freshness = e_.Freshness.ToString().ToLowerInvariant(),
                            sourceGeneratedAt = e_.SourceGeneratedAt
                        }).ToArray();
                    }
                }
            }
            catch
            {
                specContextPresent = false;
                specContextStale = false;
                evidence = [];
            }
        }

        return new
        {
            available = true,
            specId = specId.Value,
            graphReady,
            blockers,
            resultPersistence = false,
            resultAvailable = false,
            evaluationExecuted = false,
            supportedStatuses = new[] { "pass", "fail", "notVerified" },
            decisiveEvidenceSources = new[] { "build-summary", "secret-scan" },
            requirementSet = new
            {
                present = snapshot?.RequirementSet is not null,
                revision = snapshot?.RequirementSet?.Revision.Value,
                approvalStatus = FormatApprovalStatus(reqApproval)
            },
            workSpec = new
            {
                present = snapshot?.WorkSpec is not null,
                revision = snapshot?.WorkSpec?.Revision.Value,
                requirementSetRevision = snapshot?.WorkSpec?.RequirementSetRevision.Value,
                stale = snapshot?.IsWorkSpecStale ?? false,
                approvalStatus = FormatApprovalStatus(wsApproval)
            },
            implementationPlan = new
            {
                present = snapshot?.ImplementationPlan is not null,
                revision = snapshot?.ImplementationPlan?.Revision.Value,
                workSpecRevision = snapshot?.ImplementationPlan?.WorkSpecRevision.Value,
                stale = snapshot?.IsImplementationPlanStale ?? false,
                approvalStatus = FormatApprovalStatus(planApproval)
            },
            specContextPresent,
            specContextStale,
            evidence
        };
    }

    private bool ValidateTarget(string? target_, out SpecId specId, out ToolError? error)
    {
        if (string.IsNullOrWhiteSpace(target_))
        {
            specId = default;
            error = ToolError.Create(
                "SPEC_ID_REQUIRED",
                "A target SpecId is required.",
                "airepo spec init <spec-id> --apply",
                true,
                new { target = target_ ?? string.Empty });
            return false;
        }

        if (!SpecId.TryParse(target_, out specId))
        {
            error = ToolError.Create(
                "INVALID_SPEC_ID",
                $"Target '{target_}' is not a valid SpecId.",
                string.Empty,
                true,
                new { target = target_ });
            return false;
        }

        error = null;
        return true;
    }

    internal static bool IsPathContained(string root_, string path_)
    {
        string normalizedRoot = Path.GetFullPath(root_).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path_);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasReparsePointInChain(string root_, string path_)
    {
        try
        {
            string normalizedRoot = Path.GetFullPath(root_).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string current = Path.GetFullPath(path_);
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current))
                {
                    FileAttributes attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        return true;
                    }
                }

                if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                string? parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || parent == current)
                {
                    break;
                }

                current = parent;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string FormatApprovalStatus(SpecApprovalStatus status_) =>
        status_ switch
        {
            SpecApprovalStatus.Current => "current",
            SpecApprovalStatus.Stale => "stale",
            _ => "notApproved"
        };
}
