using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Spec.Lifecycle;

public sealed class SpecLifecycleService
{
    private readonly SpecWorkspace _workspace;
    private readonly SpecApprovalLedgerStore _ledgerStore;

    public SpecLifecycleService(
        string repositoryRoot_,
        SpecId specId_)
        : this(
            new SpecWorkspace(
                repositoryRoot_,
                specId_),
            new SpecApprovalLedgerStore(
                repositoryRoot_,
                specId_))
    {
    }

    public SpecLifecycleService(
        SpecWorkspace workspace_,
        SpecApprovalLedgerStore ledgerStore_)
    {
        ArgumentNullException.ThrowIfNull(
            workspace_);
        ArgumentNullException.ThrowIfNull(
            ledgerStore_);

        this._workspace =
            workspace_;
        this._ledgerStore =
            ledgerStore_;
    }

    public SpecWorkspace Workspace =>
        this._workspace;

    public SpecApprovalLedgerStore LedgerStore =>
        this._ledgerStore;

    public IReadOnlyList<SpecArtifactApprovalStatus> GetApprovalStatuses()
    {
        SpecWorkspaceSnapshot snapshot =
            this._workspace.Load();
        SpecApprovalLedger? ledger =
            this._ledgerStore.Load();

        return SpecApprovalStatusEvaluator.Evaluate(
            snapshot,
            ledger);
    }

    public SpecArtifactApprovalStatus? GetApprovalStatus(
        SpecArtifactKind artifactKind_)
    {
        SpecWorkspaceSnapshot snapshot =
            this._workspace.Load();
        SpecApprovalLedger? ledger =
            this._ledgerStore.Load();

        return SpecApprovalStatusEvaluator.Evaluate(
            artifactKind_,
            snapshot,
            ledger);
    }

    public SpecStoreResult InitializeRequirementSet(
        RequirementSet candidate_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            candidate_);
        ArgumentNullException.ThrowIfNull(
            options_);

        if (options_.ExpectedCurrentRevision is not null)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.RevisionConflict,
                "Cannot initialize RequirementSet with an expected current revision because it must not already exist.",
                SpecArtifactKind.RequirementSet);
        }

        SpecWorkspaceSnapshot snapshot =
            this._workspace.Load();

        if (snapshot.RequirementSet is not null)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.RevisionConflict,
                "A canonical RequirementSet already exists in the workspace.",
                SpecArtifactKind.RequirementSet);
        }

        return this._workspace.Store(
            candidate_,
            options_ with
            {
                ExpectedCurrentRevision =
                    null
            });
    }

    public SpecStoreResult RefineRequirementSet(
        RequirementSet candidate_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            candidate_);
        ArgumentNullException.ThrowIfNull(
            options_);

        SpecWorkspaceSnapshot snapshot =
            this._workspace.Load();

        if (snapshot.RequirementSet is null)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "A canonical RequirementSet does not exist to refine.",
                SpecArtifactKind.RequirementSet);
        }

        return this._workspace.Store(
            candidate_,
            options_);
    }

    public SpecStoreResult RefineWorkSpec(
        WorkSpec candidate_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            candidate_);
        ArgumentNullException.ThrowIfNull(
            options_);

        return this._workspace.Store(
            candidate_,
            options_);
    }

    public SpecStoreResult RefineImplementationPlan(
        ImplementationPlan candidate_,
        SpecStoreOptions options_)
    {
        ArgumentNullException.ThrowIfNull(
            candidate_);
        ArgumentNullException.ThrowIfNull(
            options_);

        SpecWorkspaceSnapshot snapshot =
            this._workspace.Load();

        RequirementSet requirementSet =
            snapshot.RequirementSet ??
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "An ImplementationPlan cannot be refined without a canonical RequirementSet.",
                SpecArtifactKind.ImplementationPlan);

        WorkSpec workSpec =
            snapshot.WorkSpec ??
            throw new SpecPersistenceException(
                SpecPersistenceException.MissingDependency,
                "An ImplementationPlan cannot be refined without a canonical WorkSpec.",
                SpecArtifactKind.ImplementationPlan);

        if (snapshot.IsWorkSpecStale)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.StaleDependency,
                "An ImplementationPlan cannot be refined against a stale canonical WorkSpec.",
                SpecArtifactKind.ImplementationPlan);
        }

        SpecWorkspaceValidator.ValidateForStore(
            candidate_,
            workSpec,
            requirementSet);

        ImplementationPlan? currentPlan =
            snapshot.ImplementationPlan;

        if (currentPlan is null)
        {
            if (options_.ExpectedCurrentRevision is not null)
            {
                throw new SpecPersistenceException(
                    SpecPersistenceException.RevisionConflict,
                    "Cannot create an initial ImplementationPlan with an expected current revision because it must not already exist.",
                    SpecArtifactKind.ImplementationPlan);
            }

            return this._workspace.Store(
                candidate_,
                options_ with
                {
                    ExpectedCurrentRevision =
                        null
                });
        }

        if (options_.ExpectedCurrentRevision is null)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.RevisionConflict,
                "Refining an existing ImplementationPlan requires the expected current revision.",
                SpecArtifactKind.ImplementationPlan);
        }

        if (options_.ExpectedCurrentRevision !=
            currentPlan.Revision)
        {
            throw new SpecPersistenceException(
                SpecPersistenceException.RevisionConflict,
                $"The expected current ImplementationPlan revision '{options_.ExpectedCurrentRevision.Value.Value}' does not match current revision '{currentPlan.Revision.Value}'.",
                SpecArtifactKind.ImplementationPlan);
        }

        return this._workspace.Store(
            candidate_,
            options_);
    }

    public SpecApprovalLedgerStoreResult ApproveRequirementSet(
        ArtifactRevision requestedRevision_,
        SpecStoreOptions ledgerOptions_)
    {
        ArgumentNullException.ThrowIfNull(
            ledgerOptions_);

        return this._ledgerStore.Append(
            allocatedId_ =>
            {
                SpecWorkspaceSnapshot snapshot =
                    this._workspace.Load();

                RequirementSet requirementSet =
                    snapshot.RequirementSet ??
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "A canonical RequirementSet does not exist to approve.",
                        SpecArtifactKind.RequirementSet);

                if (requirementSet.Revision !=
                    requestedRevision_)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.RevisionConflict,
                        $"The requested revision '{requestedRevision_.Value}' does not match current RequirementSet revision '{requirementSet.Revision.Value}'.",
                        SpecArtifactKind.RequirementSet);
                }

                return SpecApprovalBinding.Create(
                    allocatedId_,
                    requirementSet);
            },
            ledgerOptions_);
    }

    public SpecApprovalLedgerStoreResult ApproveWorkSpec(
        ArtifactRevision requestedRevision_,
        SpecStoreOptions ledgerOptions_)
    {
        ArgumentNullException.ThrowIfNull(
            ledgerOptions_);

        return this._ledgerStore.Append(
            allocatedId_ =>
            {
                SpecWorkspaceSnapshot snapshot =
                    this._workspace.Load();

                WorkSpec workSpec =
                    snapshot.WorkSpec ??
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "A canonical WorkSpec does not exist to approve.",
                        SpecArtifactKind.WorkSpec);

                if (workSpec.Revision !=
                    requestedRevision_)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.RevisionConflict,
                        $"The requested revision '{requestedRevision_.Value}' does not match current WorkSpec revision '{workSpec.Revision.Value}'.",
                        SpecArtifactKind.WorkSpec);
                }

                if (snapshot.IsWorkSpecStale)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.ApprovalPrerequisiteFailed,
                        "WorkSpec cannot be approved because it is stale relative to the canonical RequirementSet.",
                        SpecArtifactKind.WorkSpec);
                }

                RequirementSet requirementSet =
                    snapshot.RequirementSet ??
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "WorkSpec cannot be approved without a canonical RequirementSet.",
                        SpecArtifactKind.RequirementSet);

                SpecApprovalLedger? currentLedger =
                    this._ledgerStore.Load();

                IReadOnlyList<SpecArtifactApprovalStatus> statuses =
                    SpecApprovalStatusEvaluator.Evaluate(
                        snapshot,
                        currentLedger);

                SpecArtifactApprovalStatus? requirementSetStatus =
                    statuses.FirstOrDefault(
                        status_ =>
                            status_.ArtifactKind ==
                            SpecArtifactKind.RequirementSet);

                if (requirementSetStatus is null ||
                    requirementSetStatus.Status !=
                    SpecApprovalStatus.Current)
                {
                    string reason =
                        requirementSetStatus?.Status ==
                        SpecApprovalStatus.Stale
                            ? "stale"
                            : "not approved";

                    throw new SpecPersistenceException(
                        SpecPersistenceException.ApprovalPrerequisiteFailed,
                        $"WorkSpec cannot be approved because the canonical RequirementSet is {reason}.",
                        SpecArtifactKind.WorkSpec);
                }

                return SpecApprovalBinding.Create(
                    allocatedId_,
                    workSpec);
            },
            ledgerOptions_);
    }

    public SpecApprovalLedgerStoreResult ApproveImplementationPlan(
        ArtifactRevision requestedRevision_,
        SpecStoreOptions ledgerOptions_)
    {
        ArgumentNullException.ThrowIfNull(
            ledgerOptions_);

        return this._ledgerStore.Append(
            allocatedId_ =>
            {
                SpecWorkspaceSnapshot snapshot =
                    this._workspace.Load();

                ImplementationPlan implementationPlan =
                    snapshot.ImplementationPlan ??
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "A canonical ImplementationPlan does not exist to approve.",
                        SpecArtifactKind.ImplementationPlan);

                if (implementationPlan.Revision !=
                    requestedRevision_)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.RevisionConflict,
                        $"The requested revision '{requestedRevision_.Value}' does not match current ImplementationPlan revision '{implementationPlan.Revision.Value}'.",
                        SpecArtifactKind.ImplementationPlan);
                }

                if (snapshot.IsImplementationPlanStale)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.ApprovalPrerequisiteFailed,
                        "ImplementationPlan cannot be approved because it is stale relative to the canonical WorkSpec.",
                        SpecArtifactKind.ImplementationPlan);
                }

                if (snapshot.WorkSpec is null)
                {
                    throw new SpecPersistenceException(
                        SpecPersistenceException.MissingDependency,
                        "ImplementationPlan cannot be approved without a canonical WorkSpec.",
                        SpecArtifactKind.WorkSpec);
                }

                SpecApprovalLedger? currentLedger =
                    this._ledgerStore.Load();

                IReadOnlyList<SpecArtifactApprovalStatus> statuses =
                    SpecApprovalStatusEvaluator.Evaluate(
                        snapshot,
                        currentLedger);

                SpecArtifactApprovalStatus? workSpecStatus =
                    statuses.FirstOrDefault(
                        status_ =>
                            status_.ArtifactKind ==
                            SpecArtifactKind.WorkSpec);

                if (workSpecStatus is null ||
                    workSpecStatus.Status !=
                    SpecApprovalStatus.Current)
                {
                    string reason =
                        workSpecStatus?.Status ==
                        SpecApprovalStatus.Stale
                            ? "stale"
                            : "not approved";

                    throw new SpecPersistenceException(
                        SpecPersistenceException.ApprovalPrerequisiteFailed,
                        $"ImplementationPlan cannot be approved because the canonical WorkSpec is {reason}.",
                        SpecArtifactKind.ImplementationPlan);
                }

                return SpecApprovalBinding.Create(
                    allocatedId_,
                    implementationPlan);
            },
            ledgerOptions_);
    }
}
