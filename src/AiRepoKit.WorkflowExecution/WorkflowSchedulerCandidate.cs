namespace AiRepoKit.WorkflowExecution;

using System.Collections.ObjectModel;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

public sealed record WorkflowSchedulerCandidate
{
    public WorkflowPersistenceStore PersistenceStore
    {
        get;
    }

    public ExecutableWork Work
    {
        get;
    }

    public Func<string, ExecutionEnvelope> EnvelopeFactory
    {
        get;
    }

    public IReadOnlyList<WorkflowResourceClaim> ResourceClaims
    {
        get;
    }

    public Func<string, WorkflowGateProof?>? GateProofProvider
    {
        get;
    }

    public bool CancelRequested
    {
        get;
    }

    public WorkflowSchedulerCandidate(
        WorkflowPersistenceStore persistenceStore_,
        ExecutableWork work_,
        Func<string, ExecutionEnvelope> envelopeFactory_,
        IEnumerable<WorkflowResourceClaim> resourceClaims_,
        Func<string, WorkflowGateProof?>? gateProofProvider_ = null,
        bool cancelRequested_ = false)
    {
        ArgumentNullException.ThrowIfNull(
            persistenceStore_,
            nameof(persistenceStore_));
        ArgumentNullException.ThrowIfNull(
            work_,
            nameof(work_));
        ArgumentNullException.ThrowIfNull(
            envelopeFactory_,
            nameof(envelopeFactory_));
        ArgumentNullException.ThrowIfNull(
            resourceClaims_,
            nameof(resourceClaims_));

        WorkflowResourceClaim[] materializedClaims =
            resourceClaims_.ToArray();

        for (int i = 0; i < materializedClaims.Length; i++)
        {
            if (materializedClaims[i] is null)
            {
                throw new ArgumentException(
                    "Resource claims collection cannot contain null elements.",
                    nameof(resourceClaims_));
            }
        }

        if (!cancelRequested_ && materializedClaims.Length == 0)
        {
            throw new ArgumentException(
                "Non-cancellation candidate must declare at least one resource claim.",
                nameof(resourceClaims_));
        }

        HashSet<string> seenResourceIds =
            new(StringComparer.Ordinal);

        for (int i = 0; i < materializedClaims.Length; i++)
        {
            WorkflowResourceClaim claim =
                materializedClaims[i];

            if (!seenResourceIds.Add(claim.ResourceId))
            {
                throw new ArgumentException(
                    $"Duplicate resource claim detected: '{claim.ResourceId}'.",
                    nameof(resourceClaims_));
            }
        }

        for (int i = 0; i < materializedClaims.Length; i++)
        {
            for (int j = i + 1; j < materializedClaims.Length; j++)
            {
                if (materializedClaims[i].OverlapsHierarchicallyWith(materializedClaims[j]))
                {
                    throw new ArgumentException(
                        $"Hierarchical overlap detected between claims '{materializedClaims[i].ResourceId}' and '{materializedClaims[j].ResourceId}'.",
                        nameof(resourceClaims_));
                }
            }
        }

        Array.Sort(
            materializedClaims,
            static (a, b) =>
            {
                int cmp = string.CompareOrdinal(a.ResourceId, b.ResourceId);
                if (cmp != 0)
                {
                    return cmp;
                }

                return ((int) a.AccessKind).CompareTo((int) b.AccessKind);
            });

        this.PersistenceStore =
            persistenceStore_;
        this.Work =
            work_;
        this.EnvelopeFactory =
            envelopeFactory_;
        this.ResourceClaims =
            Array.AsReadOnly(materializedClaims);
        this.GateProofProvider =
            gateProofProvider_;
        this.CancelRequested =
            cancelRequested_;
    }
}
