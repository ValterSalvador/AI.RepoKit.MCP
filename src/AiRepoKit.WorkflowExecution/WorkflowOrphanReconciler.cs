namespace AiRepoKit.WorkflowExecution;

using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;

internal static class WorkflowOrphanReconciler
{
    public static async Task ReconcileOrphanWorkflowAsync(
        string storageRoot_,
        WorkflowPersistenceStore persistenceStore_,
        ExecutableWork work_,
        ExecutionEnvelope envelope_,
        IEnumerable<IAgentExecutor> executors_,
        IEnumerable<IValidationExecutor> validators_,
        WorkflowRepairPolicy repairPolicy_,
        IWorkflowSideEffectReconciler reconciler_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot_, nameof(storageRoot_));
        ArgumentNullException.ThrowIfNull(persistenceStore_, nameof(persistenceStore_));
        ArgumentNullException.ThrowIfNull(work_, nameof(work_));
        ArgumentNullException.ThrowIfNull(envelope_, nameof(envelope_));
        ArgumentNullException.ThrowIfNull(executors_, nameof(executors_));
        ArgumentNullException.ThrowIfNull(validators_, nameof(validators_));
        ArgumentNullException.ThrowIfNull(repairPolicy_, nameof(repairPolicy_));
        ArgumentNullException.ThrowIfNull(reconciler_, nameof(reconciler_));

        WorkflowSchedulerPaths paths = new(storageRoot_);

        string workflowId = persistenceStore_.WorkflowId.Value;

        // Step 0: If an OS-isolated broker is active, perform broker-coordinated maintenance
        WorkflowBrokerClient? brokerClient = null;
        try
        {
            using CancellationTokenSource probeCts = new(TimeSpan.FromMilliseconds(500));
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken_, probeCts.Token);
            brokerClient = await WorkflowBrokerClient.ConnectAsync(paths, cancellationToken_: linked.Token).ConfigureAwait(false);
        }
        catch
        {
            brokerClient = null;
        }

        if (brokerClient is not null)
        {
            await using (brokerClient.ConfigureAwait(false))
            {
                await using WorkflowBrokerMaintenanceHandle maintHandle = await brokerClient.AcquireMaintenanceLeaseAsync(
                    workflowId,
                    cancellationToken_).ConfigureAwait(false);

                try
                {
                    WorkflowCheckpointCoordinator coordinator = new(
                        persistenceStore_,
                        executors_,
                        validators_,
                        repairPolicy_,
                        reconciler_);

                    WorkflowCheckpointResult checkpointResult = await coordinator.ExecuteAsync(
                        work_,
                        envelope_,
                        cancellationToken_).ConfigureAwait(false);

                    WorkflowPersistenceSnapshot? snapshot = persistenceStore_.Load();
                    if (snapshot is null)
                    {
                        throw new InvalidOperationException(
                            $"Workflow persistence snapshot for '{workflowId}' is null following P09 reconciliation.");
                    }

                    // Complete maintenance: audited release via broker
                    await maintHandle.CompleteAsync(cancellationToken_).ConfigureAwait(false);
                    return;
                }
                catch
                {
                    // Fail closed: preserve MaintenanceInProgress/ExclusiveMaintenance on reconciliation failure;
                    // never downgrade active maintenance to Quarantined/Normal.
                    throw;
                }
            }
        }

        // Offline administration fallback: broker is inactive
        TimeSpan lockTimeout = TimeSpan.FromSeconds(5);
        try
        {
            lockTimeout = WorkflowSchedulerDomainMetadata.GetDomainLockTimeout(paths);
        }
        catch
        {
            // Default to 5 seconds
        }

        WorkflowSchedulerPolicy persistedPolicy = WorkflowSchedulerDomainMetadata.ValidatePolicyDocument(paths);

        WorkflowPersistenceSnapshot? initialSnapshot = persistenceStore_.Load();
        long initialRevision = initialSnapshot?.Revision ?? 0;

        // Step 1: Acquire domain.lock to coordinate maintenance entry
        WorkflowSchedulerLockHandle domainLock = await WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
            paths.DomainLockPath,
            lockTimeout,
            cancellationToken_).ConfigureAwait(false);

        WorkflowSchedulerLockHandle? modeLock = null;
        WorkflowSchedulerLockHandle? wfLock = null;
        string? publishedOwnerToken = null;

        try
        {
            // Verify domain integrity document
            WorkflowSchedulerDomainMetadata.ValidateIntegrityDocument(paths);

            // Step 2: Acquire domain-mode.lock exclusively (Maintenance mode) and workflow.lock
            modeLock = await WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                paths.DomainModeLockPath,
                lockTimeout,
                cancellationToken_).ConfigureAwait(false);

            wfLock = await WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                paths.GetWorkflowLockPath(workflowId),
                lockTimeout,
                cancellationToken_).ConfigureAwait(false);

            // Step 3: Publish MaintenanceInProgress manifest with global exclusion
            publishedOwnerToken = WorkflowBrokerSecurityPrimitives.GenerateToken();
            WorkflowSchedulerLeaseManifest maintManifest = new(
                workflowId,
                WorkflowSchedulerLeaseState.MaintenanceInProgress,
                publishedOwnerToken,
                slot_: null,
                domainMode_: "ExclusiveMaintenance",
                resourceClaims_: [],
                persistedPolicy.MaxConcurrentWorkflows);

            WorkflowSchedulerLeaseManifest.WriteAtomic(
                paths,
                maintManifest,
                persistedPolicy.MaxConcurrentWorkflows,
                domainLock);

            // Step 4: Release domain.lock before long-running P09 reconciliation
            domainLock.Dispose();
            domainLock = null!;

            // Step 5: Invoke the real P09 WorkflowCheckpointCoordinator
            WorkflowCheckpointCoordinator coordinator = new(
                persistenceStore_,
                executors_,
                validators_,
                repairPolicy_,
                reconciler_);

            WorkflowCheckpointResult checkpointResult = await coordinator.ExecuteAsync(
                work_,
                envelope_,
                cancellationToken_).ConfigureAwait(false);

            // Step 6: Verify canonical persistence store snapshot integrity
            WorkflowPersistenceSnapshot? snapshot = persistenceStore_.Load();
            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    $"Workflow persistence snapshot for '{workflowId}' is null following P09 reconciliation.");
            }

            // Audit P09 reconciliation evidence before allowing maintenance release!
            if (!WorkflowCoordinationBroker.VerifyP09ReconciliationEvidence(paths, workflowId, initialRevision))
            {
                throw new InvalidOperationException(
                    $"Cannot complete offline maintenance for workflow '{workflowId}': missing or incomplete P09 reconciliation evidence. Workflow remains under global exclusion.");
            }

            // Step 7: Re-acquire domain.lock for final durable metadata publication
            domainLock = await WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                paths.DomainLockPath,
                lockTimeout,
                cancellationToken_).ConfigureAwait(false);

            // Controlled maintenance manifest removal
            string activeMaintPath = paths.GetActiveLeasePath(workflowId);
            WorkflowSchedulerDomainMetadata.DeleteAtomic(
                activeMaintPath,
                paths.ActiveLeasesDirectory);

            // Controlled owner authentication removal
            try
            {
                WorkflowSchedulerOwnerAuthentication.RemoveOwnerAuthentication(
                    paths,
                    workflowId,
                    expectedOwnerToken_: publishedOwnerToken,
                    domainLockHandle_: domainLock);
            }
            catch
            {
            }

            publishedOwnerToken = null;
        }
        catch
        {
            // Fail closed: if maintenance manifest was published, ensure it preserves MaintenanceInProgress/ExclusiveMaintenance
            if (publishedOwnerToken is not null)
            {
                try
                {
                    domainLock ??= await WorkflowSchedulerLockPrimitives.AcquireExclusiveAsync(
                        paths.DomainLockPath,
                        lockTimeout,
                        CancellationToken.None).ConfigureAwait(false);

                    WorkflowSchedulerLeaseManifest preservedMaint = new(
                        workflowId,
                        WorkflowSchedulerLeaseState.MaintenanceInProgress,
                        publishedOwnerToken,
                        slot_: null,
                        domainMode_: "ExclusiveMaintenance",
                        resourceClaims_: [],
                        persistedPolicy.MaxConcurrentWorkflows);

                    WorkflowSchedulerLeaseManifest.WriteAtomic(
                        paths,
                        preservedMaint,
                        persistedPolicy.MaxConcurrentWorkflows,
                        domainLock);
                }
                catch
                {
                    // Preserve failure
                }
            }
            throw;
        }
        finally
        {
            wfLock?.Dispose();
            modeLock?.Dispose();
            domainLock?.Dispose();
        }
    }
}
