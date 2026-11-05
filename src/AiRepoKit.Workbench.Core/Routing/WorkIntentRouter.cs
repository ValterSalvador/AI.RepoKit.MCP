using System.Text.RegularExpressions;
using AiRepoKit.Workbench.Core.Models;
using AiRepoKit.Workbench.Core.Policies;

namespace AiRepoKit.Workbench.Core.Routing;

/// <summary>
/// Work Intent Router and Policy &amp; Risk Resolver.
/// Determines intent, risk, planning mode, capabilities, authorization gates,
/// and next logical stage based on trusted structured context and policy.
/// </summary>
public sealed class WorkIntentRouter : IWorkIntentRouter
{
    private static readonly Regex _shaRegex = new("^[0-9a-fA-F]{40}$", RegexOptions.Compiled);
    private readonly WorkbenchRoutingPolicy _defaultPolicy;

    public WorkIntentRouter(WorkbenchRoutingPolicy? defaultPolicy_ = null)
    {
        this._defaultPolicy = defaultPolicy_ ?? new WorkbenchRoutingPolicy();
    }

    public WorkbenchRoutingDecision Route(
        WorkbenchRoutingRequest request_,
        WorkbenchRoutingPolicy? policy_ = null)
    {
        ArgumentNullException.ThrowIfNull(request_);

        WorkbenchRoutingPolicy effectivePolicy = policy_ ?? this._defaultPolicy;
        List<string> diagnostics = [];
        List<string> missingInfo = [];
        List<WorkbenchCapability> requiredCapabilities = [];
        List<AuthorizationGate> requiredGates = [];

        // 1. Validate contract version
        if (!string.Equals(request_.ContractVersion, WorkbenchRoutingRequest.DefaultContractVersion, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add($"Unsupported contract version '{request_.ContractVersion}'. Expected '{WorkbenchRoutingRequest.DefaultContractVersion}'.");
            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: WorkIntent.Status,
                planningMode_: PlanningMode.FORMAL_SDD,
                assessedRisk_: RiskLevel.UNKNOWN,
                decisionStatus_: DecisionStatus.BLOCKED,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities,
                requiredAuthorizationGates_: requiredGates,
                nextLogicalStage_: WorkbenchStage.Halted);
        }

        // 2. Validate repository identity
        if (string.IsNullOrWhiteSpace(request_.RepositoryId))
        {
            diagnostics.Add("Repository identity cannot be empty.");
            missingInfo.Add("RepositoryId");
        }

        // 3. Validate observed baseline SHA
        if (string.IsNullOrWhiteSpace(request_.ObservedBaselineSha) || !_shaRegex.IsMatch(request_.ObservedBaselineSha))
        {
            diagnostics.Add($"Observed baseline SHA '{request_.ObservedBaselineSha}' is invalid or missing. A 40-character hex SHA is required.");
            missingInfo.Add("ObservedBaselineSha");
        }

        // 4. Validate Structured Context Metadata (B02: Null/Blank/Malformed detection)
        bool malformedMetadata = false;
        if (request_.StructuredContext != null)
        {
            // Validate AffectedFiles
            foreach (string file in request_.StructuredContext.AffectedFiles)
            {
                if (file == null)
                {
                    diagnostics.Add("Malformed affected file entry: path cannot be null.");
                    missingInfo.Add("AffectedFiles");
                    malformedMetadata = true;
                }
                else if (string.IsNullOrWhiteSpace(file))
                {
                    diagnostics.Add("Malformed affected file entry: path cannot be blank or whitespace.");
                    missingInfo.Add("AffectedFiles");
                    malformedMetadata = true;
                }
                else if (file.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    diagnostics.Add($"Malformed affected file entry '{file}': path contains invalid characters.");
                    missingInfo.Add("AffectedFiles");
                    malformedMetadata = true;
                }
            }

            // Validate Approvals
            foreach (WorkbenchApproval approval in request_.StructuredContext.Approvals)
            {
                if (approval == null)
                {
                    diagnostics.Add("Malformed approval record: approval entry cannot be null.");
                    missingInfo.Add("Approvals");
                    malformedMetadata = true;
                }
                else if (string.IsNullOrWhiteSpace(approval.ApprovalId) || string.IsNullOrWhiteSpace(approval.Role))
                {
                    diagnostics.Add("Malformed approval record: ApprovalId and Role must be non-empty.");
                    missingInfo.Add("Approvals");
                    malformedMetadata = true;
                }
            }
        }

        if (malformedMetadata)
        {
            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: WorkIntent.Status,
                planningMode_: PlanningMode.FORMAL_SDD,
                assessedRisk_: RiskLevel.UNKNOWN,
                decisionStatus_: DecisionStatus.BLOCKED,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities,
                requiredAuthorizationGates_: requiredGates,
                nextLogicalStage_: WorkbenchStage.Halted);
        }

        // 5. Resolve Work Intent and validate Legacy Commands (C06, R05)
        WorkIntent resolvedIntent = WorkIntent.Status;
        bool intentResolved = false;
        bool isLegacySpecPlan = false;

        if (!string.IsNullOrWhiteSpace(request_.StructuredContext?.LegacyCommand))
        {
            string legacy = request_.StructuredContext.LegacyCommand.Trim();
            WorkIntent? legacyResolvedIntent = null;

            if (string.Equals(legacy, "airepo plan", StringComparison.OrdinalIgnoreCase))
            {
                legacyResolvedIntent = WorkIntent.Plan;
            }
            else if (string.Equals(legacy, "airepo spec plan", StringComparison.OrdinalIgnoreCase))
            {
                legacyResolvedIntent = WorkIntent.Plan;
                isLegacySpecPlan = true;
            }
            else
            {
                diagnostics.Add($"Unknown or invalid legacy command '{legacy}'. Supported legacy commands are 'airepo plan' and 'airepo spec plan'.");
                missingInfo.Add("LegacyCommand");
                return new WorkbenchRoutingDecision(
                    contractVersion_: request_.ContractVersion,
                    repositoryId_: request_.RepositoryId,
                    observedBaselineSha_: request_.ObservedBaselineSha,
                    policyVersion_: effectivePolicy.PolicyVersion,
                    resolvedIntent_: WorkIntent.Status,
                    planningMode_: PlanningMode.FORMAL_SDD,
                    assessedRisk_: RiskLevel.UNKNOWN,
                    decisionStatus_: DecisionStatus.BLOCKED,
                    diagnosticReasons_: diagnostics,
                    missingInformation_: missingInfo,
                    requiredCapabilities_: requiredCapabilities,
                    requiredAuthorizationGates_: requiredGates,
                    nextLogicalStage_: WorkbenchStage.Halted);
            }

            // R05: Reject invalid explicit intent supplied alongside valid legacy command
            if (!string.IsNullOrWhiteSpace(request_.Intent))
            {
                if (!WorkIntentParser.TryParse(request_.Intent, out WorkIntent explicitIntent))
                {
                    diagnostics.Add($"Invalid explicit intent '{request_.Intent}' provided alongside legacy command '{legacy}'. Deterministic validation failure.");
                    missingInfo.Add("Intent");
                    return new WorkbenchRoutingDecision(
                        contractVersion_: request_.ContractVersion,
                        repositoryId_: request_.RepositoryId,
                        observedBaselineSha_: request_.ObservedBaselineSha,
                        policyVersion_: effectivePolicy.PolicyVersion,
                        resolvedIntent_: WorkIntent.Status,
                        planningMode_: PlanningMode.FORMAL_SDD,
                        assessedRisk_: RiskLevel.UNKNOWN,
                        decisionStatus_: DecisionStatus.BLOCKED,
                        diagnosticReasons_: diagnostics,
                        missingInformation_: missingInfo,
                        requiredCapabilities_: requiredCapabilities,
                        requiredAuthorizationGates_: requiredGates,
                        nextLogicalStage_: WorkbenchStage.Halted);
                }

                if (explicitIntent != legacyResolvedIntent.Value)
                {
                    diagnostics.Add($"Conflicting intents detected: legacy command '{legacy}' resolves to '{legacyResolvedIntent.Value}', but explicit intent is '{explicitIntent}'.");
                    missingInfo.Add("Intent");
                    return new WorkbenchRoutingDecision(
                        contractVersion_: request_.ContractVersion,
                        repositoryId_: request_.RepositoryId,
                        observedBaselineSha_: request_.ObservedBaselineSha,
                        policyVersion_: effectivePolicy.PolicyVersion,
                        resolvedIntent_: explicitIntent,
                        planningMode_: PlanningMode.FORMAL_SDD,
                        assessedRisk_: RiskLevel.UNKNOWN,
                        decisionStatus_: DecisionStatus.BLOCKED,
                        diagnosticReasons_: diagnostics,
                        missingInformation_: missingInfo,
                        requiredCapabilities_: requiredCapabilities,
                        requiredAuthorizationGates_: requiredGates,
                        nextLogicalStage_: WorkbenchStage.Halted);
                }
            }

            resolvedIntent = legacyResolvedIntent.Value;
            intentResolved = true;
        }

        if (!intentResolved && !string.IsNullOrWhiteSpace(request_.Intent))
        {
            if (WorkIntentParser.TryParse(request_.Intent, out WorkIntent parsed))
            {
                resolvedIntent = parsed;
                intentResolved = true;
            }
            else
            {
                diagnostics.Add($"Unrecognized work intent '{request_.Intent}'. Recognized intents are: plan, fix, do, agentic, review, qa, status.");
                missingInfo.Add("Intent");
            }
        }
        else if (!intentResolved && string.IsNullOrWhiteSpace(request_.StructuredContext?.LegacyCommand))
        {
            diagnostics.Add("Work intent was not specified.");
            missingInfo.Add("Intent");
        }

        // If basic input validation failed, fail early with deterministic diagnostics
        if (diagnostics.Count > 0 && (missingInfo.Contains("RepositoryId") || missingInfo.Contains("ObservedBaselineSha") || missingInfo.Contains("Intent")))
        {
            DecisionStatus validationFailureStatus = missingInfo.Contains("RepositoryId") || missingInfo.Contains("ObservedBaselineSha")
                ? DecisionStatus.BLOCKED
                : DecisionStatus.NEEDS_INPUT;

            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: resolvedIntent,
                planningMode_: PlanningMode.FORMAL_SDD,
                assessedRisk_: RiskLevel.UNKNOWN,
                decisionStatus_: validationFailureStatus,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities,
                requiredAuthorizationGates_: requiredGates,
                nextLogicalStage_: validationFailureStatus == DecisionStatus.BLOCKED ? WorkbenchStage.Halted : WorkbenchStage.InputClarification);
        }

        // 6. Evaluate Security Invariants: Unauthorized approval bypass or force-push
        bool isBlocked = false;
        if (request_.StructuredContext != null)
        {
            if (request_.StructuredContext.BypassApprovalRequested)
            {
                diagnostics.Add("Unauthorized attempt to bypass approval gates is strictly blocked by policy.");
                isBlocked = true;
            }

            if (request_.StructuredContext.ForcePushRequested)
            {
                diagnostics.Add("Unauthorized attempt to force-push is strictly blocked by policy.");
                isBlocked = true;
            }
        }

        if (isBlocked)
        {
            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: resolvedIntent,
                planningMode_: PlanningMode.FORMAL_SDD,
                assessedRisk_: RiskLevel.HIGH,
                decisionStatus_: DecisionStatus.BLOCKED,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities,
                requiredAuthorizationGates_: requiredGates,
                nextLogicalStage_: WorkbenchStage.Halted);
        }

        // 7. Policy & Monotonic Multi-Risk Resolution (R03, R06, F02, B01)
        // Prompt Injection Defense: Policy evaluation strictly uses StructuredContext, ignoring untrusted TaskDescription
        RiskLevel assessedRisk;
        PlanningMode planningMode;
        ChangeScope scope = request_.StructuredContext?.Scope ?? ChangeScope.Unknown;
        bool hasDatabaseMigrationRisk = false;
        bool hasAmbiguousSchemaPath = false;

        if (resolvedIntent == WorkIntent.Status)
        {
            // R06: Status routing is read-only inspection and does not require change-scope classification
            assessedRisk = RiskLevel.LOW;
            planningMode = PlanningMode.PROMPT_ONLY;
        }
        else
        {
            switch (scope)
            {
                case ChangeScope.DocumentationOnly:
                    assessedRisk = RiskLevel.LOW;
                    planningMode = isLegacySpecPlan ? PlanningMode.FORMAL_SDD : PlanningMode.PROMPT_ONLY;
                    break;

                case ChangeScope.LocalizedBugFix:
                    assessedRisk = RiskLevel.MEDIUM;
                    planningMode = isLegacySpecPlan ? PlanningMode.FORMAL_SDD : PlanningMode.LIGHT_CONTRACT;
                    break;

                case ChangeScope.PublicApiChange:
                    assessedRisk = RiskLevel.HIGH;
                    planningMode = PlanningMode.FORMAL_SDD;
                    if (!requiredGates.Contains(AuthorizationGate.ArchitectureApproval))
                    {
                        requiredGates.Add(AuthorizationGate.ArchitectureApproval);
                    }
                    break;

                case ChangeScope.DatabaseSchemaChange:
                    assessedRisk = RiskLevel.HIGH;
                    planningMode = PlanningMode.FORMAL_SDD;
                    hasDatabaseMigrationRisk = true;
                    if (!requiredGates.Contains(AuthorizationGate.SchemaMigrationApproval))
                    {
                        requiredGates.Add(AuthorizationGate.SchemaMigrationApproval);
                    }
                    break;

                case ChangeScope.AuthenticationOrAuthorizationChange:
                    assessedRisk = RiskLevel.HIGH;
                    planningMode = PlanningMode.FORMAL_SDD;
                    if (!requiredGates.Contains(AuthorizationGate.SecurityReview))
                    {
                        requiredGates.Add(AuthorizationGate.SecurityReview);
                    }
                    break;

                case ChangeScope.InfrastructureDeployment:
                    assessedRisk = RiskLevel.HIGH;
                    planningMode = PlanningMode.FORMAL_SDD;
                    if (!requiredGates.Contains(AuthorizationGate.HumanApproval))
                    {
                        requiredGates.Add(AuthorizationGate.HumanApproval);
                    }
                    break;

                case ChangeScope.GeneralFeature:
                    assessedRisk = RiskLevel.MEDIUM;
                    planningMode = isLegacySpecPlan ? PlanningMode.FORMAL_SDD : PlanningMode.LIGHT_CONTRACT;
                    break;

                case ChangeScope.Unknown:
                default:
                    // Security Invariant 1: Unknown or insufficient risk information never silently becomes LOW
                    assessedRisk = RiskLevel.UNKNOWN;
                    planningMode = PlanningMode.FORMAL_SDD;
                    diagnostics.Add("Insufficient risk information: change scope is unknown or unspecified.");
                    missingInfo.Add("StructuredContext.Scope");
                    break;
            }

            // B01 / F02 / R03: Independent accumulation of sensitive risks across all affected paths
            if (request_.StructuredContext?.AffectedFiles != null && request_.StructuredContext.AffectedFiles.Count > 0)
            {
                foreach (string file in request_.StructuredContext.AffectedFiles)
                {
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        continue;
                    }

                    string normalizedPath = file.Replace('\\', '/');
                    string fileName = Path.GetFileName(normalizedPath);

                    // 1. Auth & Security Detection
                    bool isAuthPath = normalizedPath.Contains("/auth/", StringComparison.OrdinalIgnoreCase)
                                   || normalizedPath.StartsWith("auth/", StringComparison.OrdinalIgnoreCase)
                                   || normalizedPath.Contains("/security/", StringComparison.OrdinalIgnoreCase)
                                   || normalizedPath.StartsWith("security/", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("authcontroller", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("authservice", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("authhandler", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("authprovider", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("jwt", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("oidc", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("token", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("credential", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("identity", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("authentication", StringComparison.OrdinalIgnoreCase)
                                   || fileName.Contains("authorization", StringComparison.OrdinalIgnoreCase);

                    // 2. Unambiguous Database Schema & Migration Detection (B01)
                    bool isDatabaseMigrationPath = normalizedPath.Contains("/migrations/", StringComparison.OrdinalIgnoreCase)
                                                || normalizedPath.StartsWith("migrations/", StringComparison.OrdinalIgnoreCase)
                                                || normalizedPath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase)
                                                || normalizedPath.Contains("/database/", StringComparison.OrdinalIgnoreCase)
                                                || normalizedPath.StartsWith("database/", StringComparison.OrdinalIgnoreCase)
                                                || normalizedPath.Contains("/db/", StringComparison.OrdinalIgnoreCase)
                                                || normalizedPath.StartsWith("db/", StringComparison.OrdinalIgnoreCase);

                    // 3. Ambiguous Schema Detection (B01: contains "schema" but NOT under database/migrations/sql)
                    bool isAmbiguousSchema = !isDatabaseMigrationPath &&
                        (normalizedPath.Contains("schema", StringComparison.OrdinalIgnoreCase) ||
                         fileName.Contains("schema", StringComparison.OrdinalIgnoreCase));

                    // 4. Public API & Endpoint Detection (B01)
                    bool isPublicApiPath = normalizedPath.Contains("/api/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.StartsWith("api/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.Contains("/publicapi/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.StartsWith("publicapi/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.Contains("/endpoints/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.StartsWith("endpoints/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.Contains("/public/", StringComparison.OrdinalIgnoreCase)
                                        || normalizedPath.StartsWith("public/", StringComparison.OrdinalIgnoreCase)
                                        || fileName.Contains("controller", StringComparison.OrdinalIgnoreCase)
                                        || fileName.Contains("endpoint", StringComparison.OrdinalIgnoreCase);

                    // 5. Source Code Detection
                    bool isSourceCode = normalizedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                     || normalizedPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                                     || normalizedPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                                     || normalizedPath.StartsWith("src/", StringComparison.OrdinalIgnoreCase);

                    // Apply independent risk accumulations
                    if (isAuthPath)
                    {
                        if (assessedRisk != RiskLevel.HIGH)
                        {
                            diagnostics.Add($"Sensitive path detected: '{file}' touches authentication/security. Risk escalated to HIGH and formal SDD enforced.");
                        }
                        assessedRisk = RiskLevel.HIGH;
                        planningMode = PlanningMode.FORMAL_SDD;
                        if (!requiredGates.Contains(AuthorizationGate.SecurityReview))
                        {
                            requiredGates.Add(AuthorizationGate.SecurityReview);
                        }
                    }

                    if (isDatabaseMigrationPath)
                    {
                        if (assessedRisk != RiskLevel.HIGH)
                        {
                            diagnostics.Add($"Sensitive path detected: '{file}' touches database migrations/schema. Risk escalated to HIGH and formal SDD enforced.");
                        }
                        assessedRisk = RiskLevel.HIGH;
                        planningMode = PlanningMode.FORMAL_SDD;
                        hasDatabaseMigrationRisk = true;
                        if (!requiredGates.Contains(AuthorizationGate.SchemaMigrationApproval))
                        {
                            requiredGates.Add(AuthorizationGate.SchemaMigrationApproval);
                        }
                    }
                    else if (isAmbiguousSchema)
                    {
                        // B01: Ambiguous schema path requires clarification or reassessment
                        if (assessedRisk != RiskLevel.HIGH)
                        {
                            diagnostics.Add($"Ambiguous schema-like path detected: '{file}'. Risk escalated to HIGH; clarification required to determine whether database migration authorization is needed.");
                        }
                        assessedRisk = RiskLevel.HIGH;
                        planningMode = PlanningMode.FORMAL_SDD;
                        hasAmbiguousSchemaPath = true;
                        missingInfo.Add("SchemaClassification");
                    }

                    if (isPublicApiPath)
                    {
                        if (scope != ChangeScope.PublicApiChange && assessedRisk != RiskLevel.HIGH)
                        {
                            diagnostics.Add($"Sensitive path detected: '{file}' touches public API. Architectural risk escalated to HIGH and formal SDD enforced.");
                        }
                        assessedRisk = RiskLevel.HIGH;
                        planningMode = PlanningMode.FORMAL_SDD;
                        if (!requiredGates.Contains(AuthorizationGate.ArchitectureApproval))
                        {
                            requiredGates.Add(AuthorizationGate.ArchitectureApproval);
                        }
                    }

                    if (scope == ChangeScope.DocumentationOnly && isSourceCode)
                    {
                        if (assessedRisk == RiskLevel.LOW)
                        {
                            diagnostics.Add($"Contradiction detected: Scope is declared as DocumentationOnly, but affected files include source code '{file}'.");
                            assessedRisk = RiskLevel.MEDIUM;
                            if (planningMode == PlanningMode.PROMPT_ONLY)
                            {
                                planningMode = PlanningMode.LIGHT_CONTRACT;
                            }
                        }
                    }
                }
            }
        }

        // 8. Determine Computed Required Capabilities and Dry-Run Reductions (C05, R06, F02)
        bool isDryRun = request_.StructuredContext?.IsDryRun == true;

        // Base capability: reading repository
        requiredCapabilities.Add(WorkbenchCapability.RepositoryReading);

        if (!isDryRun)
        {
            switch (resolvedIntent)
            {
                case WorkIntent.Plan:
                    // Planning is non-mutating
                    break;

                case WorkIntent.Fix:
                case WorkIntent.Do:
                case WorkIntent.Agentic:
                    requiredCapabilities.Add(WorkbenchCapability.ProcessExecution);
                    if (scope != ChangeScope.DocumentationOnly || resolvedIntent != WorkIntent.Plan)
                    {
                        requiredCapabilities.Add(WorkbenchCapability.SourceModification);
                    }
                    if (scope == ChangeScope.DatabaseSchemaChange || hasDatabaseMigrationRisk)
                    {
                        requiredCapabilities.Add(WorkbenchCapability.DatabaseMigration);
                    }
                    if (scope == ChangeScope.InfrastructureDeployment)
                    {
                        requiredCapabilities.Add(WorkbenchCapability.Deployment);
                    }
                    break;

                case WorkIntent.Qa:
                    requiredCapabilities.Add(WorkbenchCapability.ProcessExecution);
                    diagnostics.Add("ProcessExecution capability requested for QA verification; runtime process execution authorization remains external and subject to V3-V7 execution policies.");
                    break;

                case WorkIntent.Review:
                case WorkIntent.Status:
                    // Status and Review strictly request RepositoryReading only
                    break;
            }
        }
        else
        {
            diagnostics.Add("Dry-run requested: mutating capabilities withheld.");
        }

        // 9. Enforce Policy Restrictions Against Computed Required Capabilities (C02)
        bool capabilityDeniedByPolicy = false;
        foreach (WorkbenchCapability cap in requiredCapabilities)
        {
            if (!effectivePolicy.IsCapabilityAllowed(cap))
            {
                diagnostics.Add($"Required capability '{cap}' ({cap.ToPolicyName()}) is denied by workbench routing policy.");
                capabilityDeniedByPolicy = true;
            }
        }

        if (capabilityDeniedByPolicy)
        {
            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: resolvedIntent,
                planningMode_: planningMode,
                assessedRisk_: assessedRisk,
                decisionStatus_: DecisionStatus.BLOCKED,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities.Distinct().ToArray(),
                requiredAuthorizationGates_: requiredGates.Distinct().ToArray(),
                nextLogicalStage_: WorkbenchStage.Halted);
        }

        // 10. Verify Required Execution Capabilities Against Provider Environment Support (R04)
        bool providerSupportMissing = false;
        if (!isDryRun && (resolvedIntent is WorkIntent.Fix or WorkIntent.Do or WorkIntent.Agentic or WorkIntent.Qa))
        {
            IReadOnlySet<string> supportedProviderCaps = request_.StructuredContext?.SupportedProviderCapabilities
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (WorkbenchCapability cap in requiredCapabilities)
            {
                IReadOnlyList<string> satisfyingNames = cap.GetRequiredProviderCapabilityNames();
                bool hasSupport = satisfyingNames.Any(name_ => supportedProviderCaps.Contains(name_));

                if (!hasSupport)
                {
                    diagnostics.Add($"Computed required capability '{cap}' is not supported by the active provider environment. Satisfying capabilities: [{string.Join(", ", satisfyingNames)}].");
                    providerSupportMissing = true;
                }
            }
        }

        if (request_.StructuredContext != null && request_.StructuredContext.RequiredProviderCapabilities.Count > 0)
        {
            foreach (string reqCap in request_.StructuredContext.RequiredProviderCapabilities)
            {
                if (!request_.StructuredContext.SupportedProviderCapabilities.Contains(reqCap))
                {
                    diagnostics.Add($"Required provider capability '{reqCap}' is not supported by the active provider environment.");
                    providerSupportMissing = true;
                }
            }
        }

        if (providerSupportMissing)
        {
            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: resolvedIntent,
                planningMode_: planningMode,
                assessedRisk_: assessedRisk,
                decisionStatus_: DecisionStatus.BLOCKED,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities.Distinct().ToArray(),
                requiredAuthorizationGates_: requiredGates.Distinct().ToArray(),
                nextLogicalStage_: WorkbenchStage.Halted);
        }

        // 11. Evaluate Baseline Alignment (C04)
        bool isMutatingIntent = resolvedIntent is WorkIntent.Fix or WorkIntent.Do or WorkIntent.Agentic;
        bool hasMutatingCapability = requiredCapabilities.Any(c_ => c_ is WorkbenchCapability.SourceModification
            or WorkbenchCapability.DatabaseMigration
            or WorkbenchCapability.Deployment
            or WorkbenchCapability.Commit
            or WorkbenchCapability.Push
            or WorkbenchCapability.Merge
            or WorkbenchCapability.ExternalMutation
            or WorkbenchCapability.ReleasePublication);

        bool isMutatingWork = (isMutatingIntent || hasMutatingCapability) && !isDryRun;

        // Invariant: Mandatory baseline validation cannot be disabled for mutating operations
        if (isMutatingWork && !effectivePolicy.EnforceStrictBaselineMatch)
        {
            diagnostics.Add("Mandatory security invariant violation: baseline validation cannot be disabled for mutating operations.");
            return new WorkbenchRoutingDecision(
                contractVersion_: request_.ContractVersion,
                repositoryId_: request_.RepositoryId,
                observedBaselineSha_: request_.ObservedBaselineSha,
                policyVersion_: effectivePolicy.PolicyVersion,
                resolvedIntent_: resolvedIntent,
                planningMode_: planningMode,
                assessedRisk_: assessedRisk,
                decisionStatus_: DecisionStatus.BLOCKED,
                diagnosticReasons_: diagnostics,
                missingInformation_: missingInfo,
                requiredCapabilities_: requiredCapabilities.Distinct().ToArray(),
                requiredAuthorizationGates_: requiredGates.Distinct().ToArray(),
                nextLogicalStage_: WorkbenchStage.Halted);
        }

        // Validate TargetBaselineSha
        if (isMutatingWork)
        {
            if (string.IsNullOrWhiteSpace(request_.TargetBaselineSha))
            {
                diagnostics.Add("Mutating operations require an explicit, verified TargetBaselineSha. Baseline reassessment required.");
                requiredGates.Add(AuthorizationGate.BaselineAlignment);
            }
            else if (!_shaRegex.IsMatch(request_.TargetBaselineSha))
            {
                diagnostics.Add($"TargetBaselineSha '{request_.TargetBaselineSha}' is not a valid 40-character hexadecimal SHA. Baseline reassessment required.");
                requiredGates.Add(AuthorizationGate.BaselineAlignment);
            }
            else if (!string.Equals(request_.TargetBaselineSha, request_.ObservedBaselineSha, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"Observed baseline SHA '{request_.ObservedBaselineSha}' does not match approved target baseline '{request_.TargetBaselineSha}'. Reassessment required.");
                requiredGates.Add(AuthorizationGate.BaselineAlignment);
            }
        }
        else if (!string.IsNullOrWhiteSpace(request_.TargetBaselineSha))
        {
            if (!string.Equals(request_.TargetBaselineSha, request_.ObservedBaselineSha, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"Observed baseline SHA '{request_.ObservedBaselineSha}' does not match target baseline '{request_.TargetBaselineSha}'. Reassessment required.");
                requiredGates.Add(AuthorizationGate.BaselineAlignment);
            }
        }

        // 12. Eliminate Fabricated Authorization Readiness & Enforce Authoritative Gates (R01, R02)
        // Record caller claims for diagnostic purposes
        if (request_.StructuredContext?.Approvals != null && request_.StructuredContext.Approvals.Count > 0)
        {
            foreach (WorkbenchApproval approval in request_.StructuredContext.Approvals)
            {
                if (approval.IsStale)
                {
                    diagnostics.Add($"Approval claim '{approval.ApprovalId}' for role '{approval.Role}' is marked stale.");
                    requiredGates.Add(AuthorizationGate.ApprovalReassessment);
                }
                else if (string.IsNullOrWhiteSpace(approval.ApprovedBaselineSha) ||
                         !string.Equals(approval.ApprovedBaselineSha, request_.ObservedBaselineSha, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add($"Approval claim '{approval.ApprovalId}' was granted on baseline '{approval.ApprovedBaselineSha}', which differs from observed baseline '{request_.ObservedBaselineSha}'.");
                    requiredGates.Add(AuthorizationGate.ApprovalReassessment);
                }
                else
                {
                    diagnostics.Add($"Approval claim '{approval.ApprovalId}' recorded for role '{approval.Role}'. Unverified caller claim; authoritative V3-V7 verification required.");
                }
            }
        }

        // R01: In this isolated slice, without an external authoritative verification engine:
        // Caller approval claims cannot establish execution readiness for mutating work.
        // Mutating work MUST require HumanApproval gate and keep NEEDS_APPROVAL.
        if (isMutatingWork)
        {
            if (!requiredGates.Contains(AuthorizationGate.HumanApproval))
            {
                requiredGates.Add(AuthorizationGate.HumanApproval);
            }
            diagnostics.Add("Mutating work requires authoritative V3-V7 authorization verification. Execution readiness withheld.");

            // R02: Agentic intent must ALWAYS require verified workflow coordination before execution readiness
            if (resolvedIntent == WorkIntent.Agentic)
            {
                if (!requiredGates.Contains(AuthorizationGate.WorkflowCoordination))
                {
                    requiredGates.Add(AuthorizationGate.WorkflowCoordination);
                }
                diagnostics.Add("Agentic execution requires explicit workflow coordination gate.");
            }
        }

        // 13. Check Requirement Ambiguity
        bool hasAmbiguity = request_.StructuredContext?.HasAmbiguousRequirements == true;
        if (hasAmbiguity)
        {
            diagnostics.Add("Requirements are ambiguous or incomplete; clarification is required before proceeding.");
            missingInfo.Add("RequirementsClarification");
        }

        // 14. Security Invariant: Agent completion never implies work acceptance (C07)
        if (request_.StructuredContext?.AgentExecutionCompleted == true)
        {
            diagnostics.Add("Agent execution completed. The boolean WorkAccepted flag does not constitute verified acceptance evidence; external verification acceptance gate remains required.");
            if (!requiredGates.Contains(AuthorizationGate.VerificationAcceptance))
            {
                requiredGates.Add(AuthorizationGate.VerificationAcceptance);
            }
        }

        // 15. Apply Dry-Run Gate Reductions (C05)
        if (isDryRun)
        {
            requiredGates.Remove(AuthorizationGate.HumanApproval);
            requiredGates.Remove(AuthorizationGate.SchemaMigrationApproval);
            requiredGates.Remove(AuthorizationGate.ArchitectureApproval);
            requiredGates.Remove(AuthorizationGate.WorkflowCoordination);
            requiredGates.Remove(AuthorizationGate.BaselineAlignment);
            requiredGates.Remove(AuthorizationGate.ApprovalReassessment);

            if (requiredGates.Contains(AuthorizationGate.SecurityReview))
            {
                diagnostics.Add("Dry-run requested on security-sensitive changes: mutating capabilities withheld, but SecurityReview gate is retained (fail-closed security boundary). Next stage routes to ReviewGate.");
            }

            if (requiredGates.Contains(AuthorizationGate.VerificationAcceptance))
            {
                diagnostics.Add("Dry-run requested on completed agent execution: VerificationAcceptance gate is retained. Next stage routes to VerificationAndQa.");
            }

            if (requiredGates.Count == 0)
            {
                diagnostics.Add("Dry-run simulation ready: all mutating gates reduced; next stage set to DryRunSimulation (non-executing).");
            }
        }

        // 16. Resolve DecisionStatus and NextLogicalStage
        DecisionStatus decisionStatus;
        WorkbenchStage nextStage;

        if (assessedRisk == RiskLevel.UNKNOWN || hasAmbiguity || hasAmbiguousSchemaPath || missingInfo.Count > 0)
        {
            decisionStatus = DecisionStatus.NEEDS_INPUT;
            nextStage = WorkbenchStage.InputClarification;
        }
        else if (requiredGates.Count > 0)
        {
            decisionStatus = DecisionStatus.NEEDS_APPROVAL;
            nextStage = requiredGates.Contains(AuthorizationGate.VerificationAcceptance)
                ? WorkbenchStage.VerificationAndQa
                : (requiredGates.Contains(AuthorizationGate.BaselineAlignment) || requiredGates.Contains(AuthorizationGate.ApprovalReassessment)
                    ? WorkbenchStage.ApprovalWait
                    : WorkbenchStage.ReviewGate);
        }
        else if (isDryRun)
        {
            decisionStatus = DecisionStatus.READY_FOR_NEXT_STAGE;
            nextStage = WorkbenchStage.DryRunSimulation;
        }
        else
        {
            decisionStatus = DecisionStatus.READY_FOR_NEXT_STAGE;
            nextStage = resolvedIntent switch
            {
                WorkIntent.Plan => planningMode switch
                {
                    PlanningMode.PROMPT_ONLY => WorkbenchStage.PromptExecution,
                    PlanningMode.LIGHT_CONTRACT => WorkbenchStage.LightContractDrafting,
                    _ => WorkbenchStage.SpecificationPlanning
                },
                WorkIntent.Fix => WorkbenchStage.ImplementationExecution,
                WorkIntent.Do => WorkbenchStage.ImplementationExecution,
                WorkIntent.Agentic => WorkbenchStage.ImplementationExecution,
                WorkIntent.Review => WorkbenchStage.ReviewGate,
                WorkIntent.Qa => WorkbenchStage.VerificationAndQa,
                WorkIntent.Status => WorkbenchStage.StatusReporting,
                _ => WorkbenchStage.SpecificationPlanning
            };
        }

        return new WorkbenchRoutingDecision(
            contractVersion_: request_.ContractVersion,
            repositoryId_: request_.RepositoryId,
            observedBaselineSha_: request_.ObservedBaselineSha,
            policyVersion_: effectivePolicy.PolicyVersion,
            resolvedIntent_: resolvedIntent,
            planningMode_: planningMode,
            assessedRisk_: assessedRisk,
            decisionStatus_: decisionStatus,
            diagnosticReasons_: diagnostics,
            missingInformation_: missingInfo,
            requiredCapabilities_: requiredCapabilities.Distinct().ToArray(),
            requiredAuthorizationGates_: requiredGates.Distinct().ToArray(),
            nextLogicalStage_: nextStage);
    }
}
