using System.Reflection;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecLifecycleServiceTests
{
    [Fact]
    public void InitializeRequirementSet_FirstArtifact_DryRun_PredictsRevisionOneWithoutWrite()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet candidate =
            CreateRequirementSet(revision_: 99);

        SpecStoreResult result =
            service.InitializeRequirementSet(
                candidate,
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.DryRun
                });

        Assert.True(result.Changed);
        Assert.False(result.Applied);
        Assert.Null(result.PreviousRevision);
        Assert.Equal(new ArtifactRevision(1), result.TargetRevision);
        Assert.False(File.Exists(repository.GetArtifactPath(SpecArtifactKind.RequirementSet)));
    }

    [Fact]
    public void InitializeRequirementSet_FirstArtifact_Apply_DelegatesP02RevisionOneBehavior()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet candidate =
            CreateRequirementSet(revision_: 99);

        SpecStoreResult result =
            service.InitializeRequirementSet(
                candidate,
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(result.Changed);
        Assert.True(result.Applied);
        Assert.Null(result.PreviousRevision);
        Assert.Equal(new ArtifactRevision(1), result.TargetRevision);

        string path =
            repository.GetArtifactPath(SpecArtifactKind.RequirementSet);
        Assert.True(File.Exists(path));

        // Ensure WorkSpec, ImplementationPlan, and approvals.json were NOT created
        Assert.False(File.Exists(repository.GetArtifactPath(SpecArtifactKind.WorkSpec)));
        Assert.False(File.Exists(repository.GetArtifactPath(SpecArtifactKind.ImplementationPlan)));
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void InitializeRequirementSet_WhenRequirementSetAlreadyExists_RejectsWithRevisionConflictAndNoMutation()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet initial =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            initial,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        string path =
            repository.GetArtifactPath(SpecArtifactKind.RequirementSet);
        byte[] originalBytes =
            File.ReadAllBytes(path);

        RequirementSet secondCandidate =
            CreateRequirementSet(statement_: "Different");

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.InitializeRequirementSet(
                        secondCandidate,
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply
                        }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void InitializeRequirementSet_WithExpectedCurrentRevisionNotNull_RejectsWithRevisionConflict()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet candidate =
            CreateRequirementSet();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.InitializeRequirementSet(
                        candidate,
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply,
                            ExpectedCurrentRevision = new ArtifactRevision(1)
                        }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);
    }

    [Fact]
    public void RefineRequirementSet_SemanticNoOp_RetainsExistingP02NoOp()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet initial =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            initial,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        string path =
            repository.GetArtifactPath(SpecArtifactKind.RequirementSet);
        DateTime originalTimestamp =
            File.GetLastWriteTimeUtc(path);

        RequirementSet sameSemantic =
            CreateRequirementSet(revision_: 99);

        SpecStoreResult result =
            service.RefineRequirementSet(
                sameSemantic,
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.False(result.Changed);
        Assert.False(result.Applied);
        Assert.Equal(new ArtifactRevision(1), result.PreviousRevision);
        Assert.Equal(new ArtifactRevision(1), result.TargetRevision);
    }

    [Fact]
    public void RefineRequirementSet_SemanticChange_RevisionIncrements_LedgerUnchanged_DerivedStatusesBecomeStale()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet initial =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            initial,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Approve revision 1
        SpecApprovalLedgerStoreResult appResult =
            service.ApproveRequirementSet(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(appResult.Applied);
        Assert.Equal(SpecApprovalStatus.Current, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);

        byte[] originalLedgerBytes =
            File.ReadAllBytes(repository.LedgerPath);

        // Refine with semantic change
        RequirementSet changed =
            CreateRequirementSet(statement_: "Semantic change");
        SpecStoreResult refineResult =
            service.RefineRequirementSet(
                changed,
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.True(refineResult.Changed);
        Assert.True(refineResult.Applied);
        Assert.Equal(new ArtifactRevision(1), refineResult.PreviousRevision);
        Assert.Equal(new ArtifactRevision(2), refineResult.TargetRevision);

        // Ledger is completely untouched
        Assert.Equal(originalLedgerBytes, File.ReadAllBytes(repository.LedgerPath));

        // Derived status is now STALE
        Assert.Equal(SpecApprovalStatus.Stale, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);
    }

    [Fact]
    public void RefineRequirementSet_WhenRequirementSetDoesNotExist_RejectsWithMissingDependency()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet candidate =
            CreateRequirementSet();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.RefineRequirementSet(
                        candidate,
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply
                        }));

        Assert.Equal(SpecPersistenceException.MissingDependency, exception.ErrorCode);
    }

    [Fact]
    public void RefineWorkSpec_WhileRequirementSetNotApproved_Allowed()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // RequirementSet is NOT_APPROVED
        Assert.Equal(SpecApprovalStatus.NotApproved, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);

        // Drafting WorkSpec is allowed!
        WorkSpec workSpecCandidate =
            CreateWorkSpec(requirementSetRevision_: 1);

        SpecStoreResult result =
            service.RefineWorkSpec(
                workSpecCandidate,
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(result.Changed);
        Assert.True(result.Applied);
        Assert.Equal(new ArtifactRevision(1), result.TargetRevision);
        Assert.True(File.Exists(repository.GetArtifactPath(SpecArtifactKind.WorkSpec)));

        // Both statuses exist and are NotApproved
        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            service.GetApprovalStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal(SpecApprovalStatus.NotApproved, statuses[0].Status);
        Assert.Equal(SpecApprovalStatus.NotApproved, statuses[1].Status);
    }

    [Fact]
    public void RefineWorkSpec_WhileRequirementSetNotApproved_RefinementAllowedWhenP02DependenciesValid()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpecCandidate =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpecCandidate,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Refine WorkSpec further while RequirementSet is still NotApproved
        WorkSpec refinedWorkSpec =
            CreateWorkSpec(
                revision_: 2,
                requirementSetRevision_: 1,
                criterionStatement_: "Updated criterion");

        SpecStoreResult refineResult =
            service.RefineWorkSpec(
                refinedWorkSpec,
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.True(refineResult.Changed);
        Assert.True(refineResult.Applied);
        Assert.Equal(new ArtifactRevision(1), refineResult.PreviousRevision);
        Assert.Equal(new ArtifactRevision(2), refineResult.TargetRevision);
    }

    [Fact]
    public void ApproveRequirementSet_CurrentRevision_PersistedAndBoundExactly()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecApprovalLedgerStoreResult result =
            service.ApproveRequirementSet(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(result.Changed);
        Assert.True(result.Applied);
        Assert.Equal("APR-001", result.Approval.Id.Value);
        Assert.Equal(SpecArtifactKind.RequirementSet, result.Approval.ArtifactKind);
        Assert.Equal(new ArtifactRevision(1), result.Approval.ArtifactRevision);
        Assert.True(File.Exists(repository.LedgerPath));

        Assert.Equal(SpecApprovalStatus.Current, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);
    }

    [Fact]
    public void ApproveRequirementSet_WrongRevision_RejectsWithRevisionConflictAndNoMutation()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveRequirementSet(
                        new ArtifactRevision(2),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply
                        }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void ApproveRequirementSet_WhenRequirementSetDoesNotExist_RejectsWithMissingDependency()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveRequirementSet(
                        new ArtifactRevision(1),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply
                        }));

        Assert.Equal(SpecPersistenceException.MissingDependency, exception.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void ApproveRequirementSet_ExactReapproval_IsP04bIdempotent()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecApprovalLedgerStoreResult first =
            service.ApproveRequirementSet(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(first.Changed);
        Assert.True(first.Applied);

        SpecApprovalLedgerStoreResult second =
            service.ApproveRequirementSet(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.False(second.Changed);
        Assert.False(second.Applied);
        Assert.Equal(first.Approval.Id.Value, second.Approval.Id.Value);

        SpecApprovalLedger? ledger =
            service.LedgerStore.Load();
        Assert.NotNull(ledger);
        Assert.Single(ledger.Approvals);
    }

    [Fact]
    public void ApproveWorkSpec_WhileRequirementSetNotApproved_RejectsWithApprovalPrerequisiteFailedAndNoMutation()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Attempting to approve WorkSpec when RequirementSet is NotApproved must fail!
        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveWorkSpec(
                        new ArtifactRevision(1),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply
                        }));

        Assert.Equal(SpecPersistenceException.ApprovalPrerequisiteFailed, exception.ErrorCode);
        Assert.False(File.Exists(repository.LedgerPath));
    }

    [Fact]
    public void ApproveWorkSpec_WhileRequirementSetStale_RejectsWithApprovalPrerequisiteFailedAndNoMutation()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Refine RequirementSet to make it stale
        RequirementSet changedReqSet =
            CreateRequirementSet(statement_: "Changed");
        service.RefineRequirementSet(
            changedReqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        WorkSpec updatedWorkSpec =
            CreateWorkSpec(
                revision_: 2,
                requirementSetRevision_: 2,
                criterionStatement_: "Updated criterion");
        service.RefineWorkSpec(
            updatedWorkSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        // WorkSpec approval must fail because RequirementSet is stale
        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveWorkSpec(
                        new ArtifactRevision(2),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply,
                            ExpectedCurrentRevision = new ArtifactRevision(1)
                        }));

        Assert.Equal(SpecPersistenceException.ApprovalPrerequisiteFailed, exception.ErrorCode);
    }

    [Fact]
    public void ApproveWorkSpec_WhenWorkSpecStale_RejectsWithApprovalPrerequisiteFailedAndNoMutation()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Refine RequirementSet to revision 2
        RequirementSet changedReqSet =
            CreateRequirementSet(statement_: "Changed");
        service.RefineRequirementSet(
            changedReqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        // Approve RequirementSet at revision 2 so RequirementSet is CURRENT
        service.ApproveRequirementSet(
            new ArtifactRevision(2),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        Assert.Equal(SpecApprovalStatus.Current, service.GetApprovalStatus(SpecArtifactKind.RequirementSet)!.Status);

        // However, WorkSpec was persisted against RequirementSet revision 1, so WorkSpec is now stale relative to the canonical RequirementSet revision 2!
        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveWorkSpec(
                        new ArtifactRevision(1),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply,
                            ExpectedCurrentRevision = new ArtifactRevision(1)
                        }));

        Assert.Equal(SpecPersistenceException.ApprovalPrerequisiteFailed, exception.ErrorCode);
    }

    [Fact]
    public void ApproveWorkSpec_WhenWorkSpecDoesNotExist_RejectsWithMissingDependency()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveWorkSpec(
                        new ArtifactRevision(1),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply,
                            ExpectedCurrentRevision = new ArtifactRevision(1)
                        }));

        Assert.Equal(SpecPersistenceException.MissingDependency, exception.ErrorCode);
    }

    [Fact]
    public void ApproveWorkSpec_WrongRevision_RejectsWithRevisionConflict()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveWorkSpec(
                        new ArtifactRevision(99),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply,
                            ExpectedCurrentRevision = new ArtifactRevision(1)
                        }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);
    }

    [Fact]
    public void ApproveWorkSpec_WhenRequirementSetCurrent_PersistedAndBoundExactly()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecApprovalLedgerStoreResult result =
            service.ApproveWorkSpec(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.True(result.Changed);
        Assert.True(result.Applied);
        Assert.Equal("APR-002", result.Approval.Id.Value);
        Assert.Equal(SpecArtifactKind.WorkSpec, result.Approval.ArtifactKind);
        Assert.Equal(new ArtifactRevision(1), result.Approval.ArtifactRevision);

        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            service.GetApprovalStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal(SpecApprovalStatus.Current, statuses[0].Status);
        Assert.Equal(SpecApprovalStatus.Current, statuses[1].Status);
    }

    [Fact]
    public void ApproveWorkSpec_ExactReapproval_IsP04bIdempotent()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });
        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        SpecApprovalLedgerStoreResult first =
            service.ApproveWorkSpec(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(1)
                });

        Assert.True(first.Changed);
        Assert.True(first.Applied);

        SpecApprovalLedgerStoreResult second =
            service.ApproveWorkSpec(
                new ArtifactRevision(1),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply,
                    ExpectedCurrentRevision = new ArtifactRevision(2)
                });

        Assert.False(second.Changed);
        Assert.False(second.Applied);
        Assert.Equal(first.Approval.Id.Value, second.Approval.Id.Value);
    }

    [Fact]
    public void RequirementSetChangeAfterApprovals_NoLedgerDeletionOrRewrite_ApprovalStatusesDeriveStale()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });
        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });
        service.ApproveWorkSpec(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        byte[] ledgerBytesBefore =
            File.ReadAllBytes(repository.LedgerPath);

        // Refine RequirementSet to revision 2
        RequirementSet changedReq =
            CreateRequirementSet(statement_: "Changed requirement");
        service.RefineRequirementSet(
            changedReq,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        // Ledger has NOT been modified or rewritten
        byte[] ledgerBytesAfter =
            File.ReadAllBytes(repository.LedgerPath);
        Assert.Equal(ledgerBytesBefore, ledgerBytesAfter);

        // Derived statuses are both STALE
        IReadOnlyList<SpecArtifactApprovalStatus> statuses =
            service.GetApprovalStatuses();
        Assert.Equal(2, statuses.Count);
        Assert.Equal(SpecApprovalStatus.Stale, statuses[0].Status);
        Assert.Equal(SpecApprovalStatus.Stale, statuses[1].Status);
    }

    [Fact]
    public void WorkSpecChangeAfterApproval_NoLedgerDeletionOrRewrite_WorkSpecAndPlanApprovalStatusesDeriveStale()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();
        SpecWorkspace workspace =
            repository.CreateWorkspace();
        SpecApprovalLedgerStore ledgerStore =
            repository.CreateLedgerStore();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });
        service.ApproveRequirementSet(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });
        service.ApproveWorkSpec(
            new ArtifactRevision(1),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        // Store ImplementationPlan and approve it in ledger directly via P04b ledger store
        ImplementationPlan plan =
            CreateImplementationPlan();
        workspace.Store(
            plan,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });
        ledgerStore.Append(
            allocatedId_ =>
                SpecApprovalBinding.Create(
                    allocatedId_,
                    plan),
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(2)
            });

        // All 3 are CURRENT
        IReadOnlyList<SpecArtifactApprovalStatus> initialStatuses =
            service.GetApprovalStatuses();
        Assert.Equal(3, initialStatuses.Count);
        Assert.All(initialStatuses, s => Assert.Equal(SpecApprovalStatus.Current, s.Status));

        byte[] ledgerBytesBefore =
            File.ReadAllBytes(repository.LedgerPath);

        // Refine WorkSpec with semantic change
        WorkSpec refinedWorkSpec =
            CreateWorkSpec(
                revision_: 2,
                requirementSetRevision_: 1,
                criterionStatement_: "New criterion");
        service.RefineWorkSpec(
            refinedWorkSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        // Ledger is unchanged
        Assert.Equal(ledgerBytesBefore, File.ReadAllBytes(repository.LedgerPath));

        // RequirementSet is still CURRENT, WorkSpec is STALE, Plan is STALE
        IReadOnlyList<SpecArtifactApprovalStatus> updatedStatuses =
            service.GetApprovalStatuses();
        Assert.Equal(3, updatedStatuses.Count);
        Assert.Equal(SpecApprovalStatus.Current, updatedStatuses[0].Status);
        Assert.Equal(SpecApprovalStatus.Stale, updatedStatuses[1].Status);
        Assert.Equal(SpecApprovalStatus.Stale, updatedStatuses[2].Status);
    }

    [Fact]
    public void GenericPlanStatusEvaluation_SupportedViaEvaluatorAndService()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();
        SpecWorkspace workspace =
            repository.CreateWorkspace();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        WorkSpec workSpec =
            CreateWorkSpec(requirementSetRevision_: 1);
        service.RefineWorkSpec(
            workSpec,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        ImplementationPlan plan =
            CreateImplementationPlan();
        workspace.Store(
            plan,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Plan exists with no history -> NotApproved
        SpecArtifactApprovalStatus? planStatus =
            service.GetApprovalStatus(SpecArtifactKind.ImplementationPlan);
        Assert.NotNull(planStatus);
        Assert.Equal(SpecApprovalStatus.NotApproved, planStatus.Status);
    }

    [Fact]
    public void PlanApprovalCreation_ThroughLifecycleService_HasNoSuchP04cApi()
    {
        // P04c MUST NOT expose any method that creates an ImplementationPlan approval
        MethodInfo[] methods =
            typeof(SpecLifecycleService).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        foreach (MethodInfo method in
                 methods)
        {
            Assert.DoesNotContain(
                "ApprovePlan",
                method.Name,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "ApproveImplementationPlan",
                method.Name,
                StringComparison.OrdinalIgnoreCase);

            // No method may take an ImplementationPlan to approve
            ParameterInfo[] parameters =
                method.GetParameters();

            if (method.Name.StartsWith(
                    "Approve",
                    StringComparison.Ordinal))
            {
                Assert.DoesNotContain(
                    parameters,
                    p => p.ParameterType == typeof(ImplementationPlan));
            }
        }
    }

    [Fact]
    public void Concurrency_ApprovalCannotBindSupersededRevision_BecauseValidationOccursInsideSharedLock()
    {
        using TestLifecycleRepository repository =
            new();

        SpecLifecycleService service =
            repository.CreateService();

        RequirementSet reqSet =
            CreateRequirementSet();
        service.InitializeRequirementSet(
            reqSet,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply
            });

        // Refine RequirementSet to revision 2
        RequirementSet changedReq =
            CreateRequirementSet(
                revision_: 2,
                statement_: "Changed requirement for concurrency test");
        service.RefineRequirementSet(
            changedReq,
            new SpecStoreOptions
            {
                Mode = SpecWriteMode.Apply,
                ExpectedCurrentRevision = new ArtifactRevision(1)
            });

        // An approval requested for revision 1 MUST be rejected inside the Append transaction
        SpecPersistenceException exception =
            Assert.Throws<SpecPersistenceException>(
                () =>
                    service.ApproveRequirementSet(
                        new ArtifactRevision(1),
                        new SpecStoreOptions
                        {
                            Mode = SpecWriteMode.Apply
                        }));

        Assert.Equal(SpecPersistenceException.RevisionConflict, exception.ErrorCode);

        // Verify that the ledger was NOT created (zero mutation)
        Assert.False(File.Exists(repository.LedgerPath));

        // Now approving the actual current revision (2) succeeds
        SpecApprovalLedgerStoreResult result =
            service.ApproveRequirementSet(
                new ArtifactRevision(2),
                new SpecStoreOptions
                {
                    Mode = SpecWriteMode.Apply
                });

        Assert.True(result.Applied);
        Assert.Equal(new ArtifactRevision(2), result.Approval.ArtifactRevision);
    }

    private static RequirementSet CreateRequirementSet(
        int revision_ = 1,
        string statement_ = "Requirement statement")
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(revision_),
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId("INPUT-001"),
                    Text =
                        "Input text"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId("REQ-001"),
                    Statement =
                        statement_,
                    SourceInputIds =
                    [
                        new StableEntityId("INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec(
        int revision_ = 1,
        int requirementSetRevision_ = 1,
        string criterionStatement_ = "Acceptance criterion statement")
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(revision_),
            RequirementSetRevision =
                new ArtifactRevision(requirementSetRevision_),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId("CON-001"),
                    Statement =
                        "Constraint statement",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId("AC-001"),
                    Statement =
                        criterionStatement_,
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                }
            ]
        };
    }

    private static ImplementationPlan CreateImplementationPlan(
        int revision_ = 1,
        int workSpecRevision_ = 1)
    {
        return new ImplementationPlan
        {
            Revision =
                new ArtifactRevision(revision_),
            WorkSpecRevision =
                new ArtifactRevision(workSpecRevision_),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId("PLAN-STEP-001"),
                    Statement =
                        "Step statement",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId("AC-001")
                    ]
                }
            ]
        };
    }

    private sealed class TestLifecycleRepository : IDisposable
    {
        private readonly SpecArtifactPaths _paths;

        public TestLifecycleRepository(bool createRoot = true)
        {
            this.Root =
                Path.Combine(
                    Path.GetTempPath(),
                    "airepokit-lifecycle-test-" +
                    Guid.NewGuid().ToString("N"));

            this._paths =
                new SpecArtifactPaths(
                    this.Root,
                    new SpecId("spec-sample"));

            if (createRoot)
            {
                Directory.CreateDirectory(this.Root);
            }
        }

        public string Root { get; }

        public string SpecDirectory =>
            this._paths.SpecDirectory;

        public string LedgerPath =>
            this._paths.GetApprovalLedgerPath();

        public SpecLifecycleService CreateService()
        {
            return new SpecLifecycleService(
                this.Root,
                new SpecId("spec-sample"));
        }

        public SpecWorkspace CreateWorkspace()
        {
            return new SpecWorkspace(
                this.Root,
                new SpecId("spec-sample"));
        }

        public SpecApprovalLedgerStore CreateLedgerStore()
        {
            return new SpecApprovalLedgerStore(
                this.Root,
                new SpecId("spec-sample"));
        }

        public string GetArtifactPath(SpecArtifactKind kind_)
        {
            return this._paths.GetArtifactPath(kind_);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Root))
                {
                    Directory.Delete(
                        this.Root,
                        recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
