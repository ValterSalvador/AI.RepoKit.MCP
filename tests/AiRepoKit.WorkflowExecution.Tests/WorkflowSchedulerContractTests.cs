namespace AiRepoKit.WorkflowExecution.Tests;

using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class WorkflowSchedulerContractTests : IDisposable
{
    private readonly string _tempDirectory;

    public WorkflowSchedulerContractTests()
    {
        this._tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "aik-sched-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDirectory))
        {
            try
            {
                Directory.Delete(this._tempDirectory, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup
            }
        }
    }

    // =========================================================================
    // 1. WorkflowResourceAccessKind Tests
    // =========================================================================

    [Fact]
    public void AccessKind_HasExactFrozenEnumValues()
    {
        Type enumType = typeof(WorkflowResourceAccessKind);
        Assert.True(enumType.IsEnum);

        Assert.Equal(1, (int) WorkflowResourceAccessKind.SharedRead);
        Assert.Equal(2, (int) WorkflowResourceAccessKind.ExclusiveWrite);

        string[] names = Enum.GetNames(enumType)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "ExclusiveWrite",
                "SharedRead"
            ],
            names);
    }

    // =========================================================================
    // 2. WorkflowResourceClaim Tests
    // =========================================================================

    [Fact]
    public void ResourceClaim_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowResourceClaim);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(
            [
                typeof(string),
                typeof(WorkflowResourceAccessKind)
            ],
            constructor.GetParameters().Select(p => p.ParameterType).ToArray());

        Assert.Equal(
            [
                "AccessKind",
                "ResourceId"
            ],
            DeclaredPublicProperties(type));

        Assert.Equal(
            typeof(string),
            type.GetProperty("ResourceId")!.PropertyType);
        Assert.Equal(
            typeof(WorkflowResourceAccessKind),
            type.GetProperty("AccessKind")!.PropertyType);

        Assert.Empty(DeclaredPublicMethods(type));
    }

    [Theory]
    [InlineData("fs:/", WorkflowResourceAccessKind.SharedRead)]
    [InlineData("fs:/workspace", WorkflowResourceAccessKind.ExclusiveWrite)]
    [InlineData("fs:/workspace/src/app", WorkflowResourceAccessKind.SharedRead)]
    [InlineData("fs:C:/", WorkflowResourceAccessKind.SharedRead)]
    [InlineData("fs:C:/workspace/file.txt", WorkflowResourceAccessKind.ExclusiveWrite)]
    [InlineData("logical:build", WorkflowResourceAccessKind.ExclusiveWrite)]
    [InlineData("logical:database/orders", WorkflowResourceAccessKind.SharedRead)]
    [InlineData("logical:service/cache/v1", WorkflowResourceAccessKind.ExclusiveWrite)]
    public void ResourceClaim_ValidIdentifiers_ConstructSuccessfully(
        string resourceId,
        WorkflowResourceAccessKind accessKind)
    {
        WorkflowResourceClaim claim = new(resourceId, accessKind);
        Assert.Equal(resourceId, claim.ResourceId);
        Assert.Equal(accessKind, claim.AccessKind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("path/to/resource")]
    [InlineData("http://example.com/res")]
    [InlineData("git:repo")]
    [InlineData("s3:bucket/key")]
    [InlineData("unknown:something")]
    public void ResourceClaim_MissingOrInvalidScheme_ThrowsArgumentException(string invalidId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(invalidId, WorkflowResourceAccessKind.SharedRead));
    }

    [Fact]
    public void ResourceClaim_NullResourceId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowResourceClaim(null!, WorkflowResourceAccessKind.SharedRead));
    }

    [Theory]
    [InlineData((WorkflowResourceAccessKind) 0)]
    [InlineData((WorkflowResourceAccessKind) 99)]
    [InlineData((WorkflowResourceAccessKind) (-1))]
    public void ResourceClaim_UndefinedEnum_ThrowsArgumentOutOfRangeException(WorkflowResourceAccessKind invalidKind)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowResourceClaim("fs:/valid", invalidKind));
    }

    [Theory]
    [InlineData("fs:relative/path")]
    [InlineData("fs:foo")]
    [InlineData("fs:./foo")]
    [InlineData("fs:../foo")]
    [InlineData("fs:C:relative")]
    public void ResourceClaim_FsRelativePath_ThrowsArgumentException(string relativeId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(relativeId, WorkflowResourceAccessKind.SharedRead));
    }

    [Theory]
    [InlineData("fs:.")]
    [InlineData("fs:..")]
    [InlineData("fs:/a/./b")]
    [InlineData("fs:/a/../b")]
    [InlineData("fs:/a/.")]
    [InlineData("fs:/a/..")]
    [InlineData("fs:C:/a/./b")]
    [InlineData("fs:C:/a/../b")]
    [InlineData("logical:.")]
    [InlineData("logical:..")]
    [InlineData("logical:a/./b")]
    [InlineData("logical:a/../b")]
    public void ResourceClaim_DotAndDotDotSegments_ThrowsArgumentException(string dotId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(dotId, WorkflowResourceAccessKind.SharedRead));
    }

    [Fact]
    public void ResourceClaim_NfcAndNonNfcInput_BehavesStrictly()
    {
        // Precomposed 'e' with acute: NFC
        string nfcString = "fs:/test/\u00e9";
        WorkflowResourceClaim claim = new(nfcString, WorkflowResourceAccessKind.SharedRead);
        Assert.Equal(nfcString, claim.ResourceId);

        // Decomposed 'e' followed by combining acute accent U+0301: NFD (not NFC)
        string nonNfcString = "fs:/test/e\u0301";
        Assert.False(nonNfcString.IsNormalized(NormalizationForm.FormC));

        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(nonNfcString, WorkflowResourceAccessKind.SharedRead));
    }

    [Fact]
    public void ResourceClaim_InvalidUnicodeSurrogates_ThrowsArgumentException()
    {
        // Unpaired high surrogate
        string invalidUnicode = "fs:/test/\uD800";
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(invalidUnicode, WorkflowResourceAccessKind.SharedRead));
    }

    [Theory]
    [InlineData("fs:")]
    [InlineData("fs:   ")]
    [InlineData("fs:/trailing/")]
    [InlineData("fs:/double//slash")]
    [InlineData("logical:")]
    [InlineData("logical:   ")]
    [InlineData("logical:/leading")]
    [InlineData("logical:trailing/")]
    [InlineData("logical:double//slash")]
    [InlineData("logical:with\\backslash")]
    public void ResourceClaim_MalformedOrEmptyResourceBodies_ThrowsArgumentException(string malformedId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(malformedId, WorkflowResourceAccessKind.SharedRead));
    }

    [Fact]
    public void ResourceClaim_CanonicalWindowsPath_ConstructsSuccessfully()
    {
        WorkflowResourceClaim claim = new("fs:C:/repo", WorkflowResourceAccessKind.SharedRead);
        Assert.Equal("fs:C:/repo", claim.ResourceId);
        Assert.Equal(WorkflowResourceAccessKind.SharedRead, claim.AccessKind);
    }

    [Theory]
    [InlineData("fs:C:\\repo")]
    [InlineData("fs:C:\\workspace\\file.txt")]
    [InlineData("fs:\\repo")]
    [InlineData("fs:/repo\\sub")]
    public void ResourceClaim_BackslashInFsPath_ThrowsArgumentException(string backslashId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(backslashId, WorkflowResourceAccessKind.SharedRead));
    }

    [Theory]
    [InlineData("fs:c:/repo")]
    [InlineData("fs:c:/workspace")]
    [InlineData("fs:d:/data")]
    public void ResourceClaim_LowercaseWindowsDriveLetter_ThrowsArgumentException(string lowercaseDriveId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(lowercaseDriveId, WorkflowResourceAccessKind.SharedRead));
    }

    [Fact]
    public void ResourceClaim_WindowsPaths_HierarchicalOverlap_Detected()
    {
        WorkflowResourceClaim parent = new("fs:C:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim child = new("fs:C:/repo/src", WorkflowResourceAccessKind.ExclusiveWrite);

        Assert.True(parent.OverlapsHierarchicallyWith(child));
        Assert.True(child.OverlapsHierarchicallyWith(parent));

        WorkflowPersistenceStore store = this.CreateStore("wf-overlap");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, [parent, child]));
    }

    [Fact]
    public void ResourceClaim_WindowsPaths_SegmentBoundaries_DoNotOverlap()
    {
        WorkflowResourceClaim claim1 = new("fs:C:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim claim2 = new("fs:C:/repo-other", WorkflowResourceAccessKind.ExclusiveWrite);

        Assert.False(claim1.OverlapsHierarchicallyWith(claim2));
        Assert.False(claim2.OverlapsHierarchicallyWith(claim1));

        WorkflowPersistenceStore store = this.CreateStore("wf-no-overlap");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        WorkflowSchedulerCandidate candidate = new(store, work, factory, [claim1, claim2]);
        Assert.Equal(2, candidate.ResourceClaims.Count);
    }

    [Theory]
    [InlineData("fs:/repo/a\0b")]
    [InlineData("fs:C:/repo/a\0b")]
    [InlineData("logical:a\0b")]
    [InlineData("logical:database/\0/table")]
    public void ResourceClaim_NulCharacters_ThrowsArgumentException(string nulId)
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowResourceClaim(nulId, WorkflowResourceAccessKind.SharedRead));
    }

    [Fact]
    public void ResourceClaim_ValueEquality_SameProperties_AreEqualAndHaveSameHashCode()
    {
        WorkflowResourceClaim fs1 = new("fs:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim fs2 = new("fs:/repo", WorkflowResourceAccessKind.SharedRead);

        Assert.Equal(fs1, fs2);
        Assert.True(fs1 == fs2);
        Assert.False(fs1 != fs2);
        Assert.True(fs1.Equals(fs2));
        Assert.True(fs1.Equals((object) fs2));
        Assert.Equal(fs1.GetHashCode(), fs2.GetHashCode());

        WorkflowResourceClaim log1 = new("logical:database/orders", WorkflowResourceAccessKind.ExclusiveWrite);
        WorkflowResourceClaim log2 = new("logical:database/orders", WorkflowResourceAccessKind.ExclusiveWrite);

        Assert.Equal(log1, log2);
        Assert.True(log1 == log2);
        Assert.False(log1 != log2);
        Assert.True(log1.Equals(log2));
        Assert.True(log1.Equals((object) log2));
        Assert.Equal(log1.GetHashCode(), log2.GetHashCode());

        WorkflowResourceClaim win1 = new("fs:C:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim win2 = new("fs:C:/repo", WorkflowResourceAccessKind.SharedRead);

        Assert.Equal(win1, win2);
        Assert.True(win1 == win2);
        Assert.False(win1 != win2);
        Assert.True(win1.Equals(win2));
        Assert.True(win1.Equals((object) win2));
        Assert.Equal(win1.GetHashCode(), win2.GetHashCode());
    }

    [Fact]
    public void ResourceClaim_ValueEquality_HashSetDeduplicatesByValue()
    {
        WorkflowResourceClaim fs1 = new("fs:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim fs2 = new("fs:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim log1 = new("logical:database", WorkflowResourceAccessKind.ExclusiveWrite);
        WorkflowResourceClaim log2 = new("logical:database", WorkflowResourceAccessKind.ExclusiveWrite);

        HashSet<WorkflowResourceClaim> set = [fs1, fs2, log1, log2];

        Assert.Equal(2, set.Count);
        Assert.Contains(fs1, set);
        Assert.Contains(fs2, set);
        Assert.Contains(log1, set);
        Assert.Contains(log2, set);
    }

    [Fact]
    public void ResourceClaim_ValueEquality_DistinctResourceId_ProducesInequality()
    {
        WorkflowResourceClaim fs1 = new("fs:/repo/a", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim fs2 = new("fs:/repo/b", WorkflowResourceAccessKind.SharedRead);

        Assert.NotEqual(fs1, fs2);
        Assert.False(fs1 == fs2);
        Assert.True(fs1 != fs2);
        Assert.False(fs1.Equals(fs2));
        Assert.False(fs1.Equals((object) fs2));

        WorkflowResourceClaim log1 = new("logical:orders", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim log2 = new("logical:users", WorkflowResourceAccessKind.SharedRead);

        Assert.NotEqual(log1, log2);
        Assert.False(log1 == log2);
        Assert.True(log1 != log2);
        Assert.False(log1.Equals(log2));
        Assert.False(log1.Equals((object) log2));
    }

    [Fact]
    public void ResourceClaim_ValueEquality_DistinctAccessKind_ProducesInequality()
    {
        WorkflowResourceClaim fs1 = new("fs:/repo", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim fs2 = new("fs:/repo", WorkflowResourceAccessKind.ExclusiveWrite);

        Assert.NotEqual(fs1, fs2);
        Assert.False(fs1 == fs2);
        Assert.True(fs1 != fs2);
        Assert.False(fs1.Equals(fs2));
        Assert.False(fs1.Equals((object) fs2));

        WorkflowResourceClaim log1 = new("logical:build", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim log2 = new("logical:build", WorkflowResourceAccessKind.ExclusiveWrite);

        Assert.NotEqual(log1, log2);
        Assert.False(log1 == log2);
        Assert.True(log1 != log2);
        Assert.False(log1.Equals(log2));
        Assert.False(log1.Equals((object) log2));
    }

    // =========================================================================
    // 3. WorkflowSchedulerPolicy Tests
    // =========================================================================

    [Fact]
    public void SchedulerPolicy_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowSchedulerPolicy);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = constructor.GetParameters();

        Assert.Equal(
            [
                typeof(int),
                typeof(bool),
                typeof(bool),
                typeof(TimeSpan?)
            ],
            parameters.Select(p => p.ParameterType).ToArray());

        Assert.False(parameters[0].HasDefaultValue);
        Assert.True(parameters[1].HasDefaultValue);
        Assert.Equal(false, parameters[1].DefaultValue);
        Assert.True(parameters[2].HasDefaultValue);
        Assert.Equal(true, parameters[2].DefaultValue);
        Assert.True(parameters[3].HasDefaultValue);
        Assert.Null(parameters[3].DefaultValue);

        Assert.Equal(
            [
                "AllowUnrestrictedExecution",
                "ContinueUntilBlockedOrTerminal",
                "DomainLockTimeout",
                "MaxConcurrentWorkflows"
            ],
            DeclaredPublicProperties(type));

        Assert.Empty(DeclaredPublicMethods(type));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(32)]
    public void SchedulerPolicy_ValidCapacityBounds_ConstructsSuccessfully(int capacity)
    {
        WorkflowSchedulerPolicy policy = new(capacity);
        Assert.Equal(capacity, policy.MaxConcurrentWorkflows);
        Assert.False(policy.AllowUnrestrictedExecution);
        Assert.True(policy.ContinueUntilBlockedOrTerminal);
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DomainLockTimeout);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(33)]
    [InlineData(100)]
    public void SchedulerPolicy_InvalidCapacityBounds_ThrowsArgumentOutOfRangeException(int invalidCapacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowSchedulerPolicy(invalidCapacity));
    }

    [Fact]
    public void SchedulerPolicy_DefaultDomainLockTimeout_IsFiveSeconds()
    {
        WorkflowSchedulerPolicy policy = new(4, domainLockTimeout_: null);
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DomainLockTimeout);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(500)]
    [InlineData(10000)]
    public void SchedulerPolicy_PositiveTimeout_ConstructsSuccessfully(int milliseconds)
    {
        TimeSpan timeout = TimeSpan.FromMilliseconds(milliseconds);
        WorkflowSchedulerPolicy policy = new(4, domainLockTimeout_: timeout);
        Assert.Equal(timeout, policy.DomainLockTimeout);
    }

    [Fact]
    public void SchedulerPolicy_ZeroTimeout_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowSchedulerPolicy(4, domainLockTimeout_: TimeSpan.Zero));
    }

    [Fact]
    public void SchedulerPolicy_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowSchedulerPolicy(4, domainLockTimeout_: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void SchedulerPolicy_ExplicitFlags_Preserved()
    {
        WorkflowSchedulerPolicy policy = new(
            maxConcurrentWorkflows_: 8,
            allowUnrestrictedExecution_: true,
            continueUntilBlockedOrTerminal_: false,
            domainLockTimeout_: TimeSpan.FromSeconds(15));

        Assert.Equal(8, policy.MaxConcurrentWorkflows);
        Assert.True(policy.AllowUnrestrictedExecution);
        Assert.False(policy.ContinueUntilBlockedOrTerminal);
        Assert.Equal(TimeSpan.FromSeconds(15), policy.DomainLockTimeout);
    }

    // =========================================================================
    // 4. WorkflowSchedulerCandidate Tests
    // =========================================================================

    [Fact]
    public void SchedulerCandidate_PublicSurfaceIsFrozen()
    {
        Type type = typeof(WorkflowSchedulerCandidate);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        ParameterInfo[] parameters = constructor.GetParameters();

        Assert.Equal(
            [
                typeof(WorkflowPersistenceStore),
                typeof(ExecutableWork),
                typeof(Func<string, ExecutionEnvelope>),
                typeof(IEnumerable<WorkflowResourceClaim>),
                typeof(Func<string, WorkflowGateProof?>),
                typeof(bool)
            ],
            parameters.Select(p => p.ParameterType).ToArray());

        Assert.False(parameters[0].HasDefaultValue);
        Assert.False(parameters[1].HasDefaultValue);
        Assert.False(parameters[2].HasDefaultValue);
        Assert.False(parameters[3].HasDefaultValue);
        Assert.True(parameters[4].HasDefaultValue);
        Assert.Null(parameters[4].DefaultValue);
        Assert.True(parameters[5].HasDefaultValue);
        Assert.Equal(false, parameters[5].DefaultValue);

        Assert.Equal(
            [
                "CancelRequested",
                "EnvelopeFactory",
                "GateProofProvider",
                "PersistenceStore",
                "ResourceClaims",
                "Work"
            ],
            DeclaredPublicProperties(type));

        Assert.Equal(
            typeof(WorkflowPersistenceStore),
            type.GetProperty("PersistenceStore")!.PropertyType);
        Assert.Equal(
            typeof(ExecutableWork),
            type.GetProperty("Work")!.PropertyType);
        Assert.Equal(
            typeof(Func<string, ExecutionEnvelope>),
            type.GetProperty("EnvelopeFactory")!.PropertyType);
        Assert.Equal(
            typeof(IReadOnlyList<WorkflowResourceClaim>),
            type.GetProperty("ResourceClaims")!.PropertyType);
        Assert.Equal(
            typeof(Func<string, WorkflowGateProof?>),
            type.GetProperty("GateProofProvider")!.PropertyType);
        Assert.Equal(
            typeof(bool),
            type.GetProperty("CancelRequested")!.PropertyType);

        Assert.Empty(DeclaredPublicMethods(type));
    }

    [Fact]
    public void Candidate_Constructor_ThrowsOnNullMandatoryArguments()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);
        WorkflowResourceClaim[] claims = [new("fs:/a", WorkflowResourceAccessKind.SharedRead)];

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowSchedulerCandidate(null!, work, factory, claims));
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowSchedulerCandidate(store, null!, factory, claims));
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowSchedulerCandidate(store, work, null!, claims));
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, null!));
    }

    [Fact]
    public void Candidate_NullElementInClaims_ThrowsArgumentException()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);
        WorkflowResourceClaim[] claimsWithNull =
        [
            new("fs:/a", WorkflowResourceAccessKind.SharedRead),
            null!
        ];

        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, claimsWithNull));
    }

    [Fact]
    public void Candidate_EmptyClaims_ThrowsWhenNotCancellation()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, [], cancelRequested_: false));
    }

    [Fact]
    public void Candidate_EmptyClaims_AllowedWhenCancelRequested()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            [],
            cancelRequested_: true);

        Assert.True(candidate.CancelRequested);
        Assert.Empty(candidate.ResourceClaims);
    }

    [Fact]
    public void Candidate_DuplicateClaims_ThrowsArgumentException()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        WorkflowResourceClaim[] duplicatesSameAccess =
        [
            new("fs:/workspace", WorkflowResourceAccessKind.SharedRead),
            new("fs:/workspace", WorkflowResourceAccessKind.SharedRead)
        ];

        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, duplicatesSameAccess));

        WorkflowResourceClaim[] duplicatesDifferentAccess =
        [
            new("logical:database", WorkflowResourceAccessKind.SharedRead),
            new("logical:database", WorkflowResourceAccessKind.ExclusiveWrite)
        ];

        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, duplicatesDifferentAccess));
    }

    [Fact]
    public void Candidate_AncestorAndDescendantClaims_ThrowsArgumentException()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        // fs: parent and child
        WorkflowResourceClaim[] fsParentChild =
        [
            new("fs:/workspace", WorkflowResourceAccessKind.ExclusiveWrite),
            new("fs:/workspace/src", WorkflowResourceAccessKind.SharedRead)
        ];
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, fsParentChild));

        // logical: parent and child
        WorkflowResourceClaim[] logicalParentChild =
        [
            new("logical:db", WorkflowResourceAccessKind.SharedRead),
            new("logical:db/tables/orders", WorkflowResourceAccessKind.ExclusiveWrite)
        ];
        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, logicalParentChild));
    }

    [Fact]
    public void Candidate_OverlappingSharedReadSharedRead_ThrowsArgumentException()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        // Both SharedRead must still be rejected per frozen invariant
        WorkflowResourceClaim[] sharedOverlap =
        [
            new("fs:/workspace", WorkflowResourceAccessKind.SharedRead),
            new("fs:/workspace/src", WorkflowResourceAccessKind.SharedRead)
        ];

        Assert.Throws<ArgumentException>(() =>
            new WorkflowSchedulerCandidate(store, work, factory, sharedOverlap));
    }

    [Fact]
    public void Candidate_IndependentResources_ConstructsSuccessfully()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        WorkflowResourceClaim[] independentClaims =
        [
            new("fs:/workspace/src", WorkflowResourceAccessKind.ExclusiveWrite),
            new("fs:/workspace/docs", WorkflowResourceAccessKind.SharedRead),
            new("logical:database/orders", WorkflowResourceAccessKind.ExclusiveWrite),
            new("logical:database/users", WorkflowResourceAccessKind.SharedRead),
            new("fs:C:/data", WorkflowResourceAccessKind.SharedRead),
            new("fs:D:/data", WorkflowResourceAccessKind.ExclusiveWrite)
        ];

        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            independentClaims);

        Assert.Equal(6, candidate.ResourceClaims.Count);
    }

    [Fact]
    public void Candidate_SegmentBoundaries_DoNotCreateFalseConflict()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        // 'fs:/repo' and 'fs:/repo-other' share a textual prefix but NOT segment boundary
        WorkflowResourceClaim[] claims =
        [
            new("fs:/repo", WorkflowResourceAccessKind.SharedRead),
            new("fs:/repo-other", WorkflowResourceAccessKind.ExclusiveWrite),
            new("logical:app", WorkflowResourceAccessKind.SharedRead),
            new("logical:app2", WorkflowResourceAccessKind.ExclusiveWrite)
        ];

        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            claims);

        Assert.Equal(4, candidate.ResourceClaims.Count);
    }

    [Fact]
    public void Candidate_SnapshotImmutability_CallerModificationsDoNotAffectCandidate()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        List<WorkflowResourceClaim> mutableList =
        [
            new("fs:/workspace/src", WorkflowResourceAccessKind.ExclusiveWrite),
            new("fs:/workspace/docs", WorkflowResourceAccessKind.SharedRead)
        ];

        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            mutableList);

        Assert.Equal(2, candidate.ResourceClaims.Count);

        // Caller adds and clears
        mutableList.Add(new("fs:/workspace/tests", WorkflowResourceAccessKind.SharedRead));
        mutableList.Clear();

        Assert.Equal(2, candidate.ResourceClaims.Count);
        Assert.Equal("fs:/workspace/docs", candidate.ResourceClaims[0].ResourceId);
        Assert.Equal("fs:/workspace/src", candidate.ResourceClaims[1].ResourceId);

        // Verify candidate.ResourceClaims cannot be modified as an array
        Assert.IsType<ReadOnlyCollection<WorkflowResourceClaim>>(candidate.ResourceClaims);
    }

    [Fact]
    public void Candidate_DeterministicOrdering_OrdersClaimsByOrdinalResourceId()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);

        WorkflowResourceClaim claimZ = new("logical:z", WorkflowResourceAccessKind.SharedRead);
        WorkflowResourceClaim claimM = new("logical:m", WorkflowResourceAccessKind.ExclusiveWrite);
        WorkflowResourceClaim claimA = new("fs:/a", WorkflowResourceAccessKind.SharedRead);

        // Pass in arbitrary order: Z, A, M
        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            [claimZ, claimA, claimM]);

        Assert.Equal("fs:/a", candidate.ResourceClaims[0].ResourceId);
        Assert.Equal("logical:m", candidate.ResourceClaims[1].ResourceId);
        Assert.Equal("logical:z", candidate.ResourceClaims[2].ResourceId);
    }

    [Fact]
    public void Candidate_CallbacksNotInvokedInConstructor()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();

        bool factoryInvoked = false;
        Func<string, ExecutionEnvelope> factory = taskId =>
        {
            factoryInvoked = true;
            throw new InvalidOperationException("Factory should not be invoked in constructor.");
        };

        bool gateProofProviderInvoked = false;
        Func<string, WorkflowGateProof?> proofProvider = taskId =>
        {
            gateProofProviderInvoked = true;
            throw new InvalidOperationException("GateProofProvider should not be invoked in constructor.");
        };

        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            [new("fs:/workspace", WorkflowResourceAccessKind.SharedRead)],
            proofProvider);

        Assert.False(factoryInvoked);
        Assert.False(gateProofProviderInvoked);
        Assert.NotNull(candidate);
    }

    [Fact]
    public void Candidate_PreservesSuppliedDependencyReferences()
    {
        WorkflowPersistenceStore store = this.CreateStore("wf-1");
        ExecutableWork work = CreateWork();
        Func<string, ExecutionEnvelope> factory = taskId => CreateEnvelope(work, taskId);
        Func<string, WorkflowGateProof?> proofProvider = taskId => null;
        WorkflowResourceClaim claim = new("fs:/workspace", WorkflowResourceAccessKind.SharedRead);

        WorkflowSchedulerCandidate candidate = new(
            store,
            work,
            factory,
            [claim],
            proofProvider,
            cancelRequested_: true);

        Assert.Same(store, candidate.PersistenceStore);
        Assert.Same(work, candidate.Work);
        Assert.Same(factory, candidate.EnvelopeFactory);
        Assert.Same(proofProvider, candidate.GateProofProvider);
        Assert.True(candidate.CancelRequested);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private WorkflowPersistenceStore CreateStore(string workflowId)
    {
        return new WorkflowPersistenceStore(
            this._tempDirectory,
            new WorkflowId(workflowId));
    }

    private static ExecutableWork CreateWork(
        string taskId = "task-1",
        int revision = 1)
    {
        return new ExecutableWork(
            revision,
            [
                new ExecutableTask(
                    taskId,
                    "step-1",
                    "do work")
            ],
            [],
            [],
            [],
            []);
    }

    private static ExecutionEnvelope CreateEnvelope(
        ExecutableWork work_,
        string taskId_ = "task-1")
    {
        CompiledPrompt prompt = new(
            taskId_,
            "instruction");

        return ExecutionEnvelope.Create(
            work_,
            prompt,
            ExecutionPermission.ReadOnly,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);
    }

    private static MethodInfo[] DeclaredPublicMethods(Type type_)
    {
        return type_
            .GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Where(method_ =>
                !method_.IsSpecialName &&
                method_.Name != "Equals" &&
                method_.Name != "GetHashCode" &&
                method_.Name != "PrintMembers" &&
                method_.Name != "ToString" &&
                !method_.Name.StartsWith("<Clone>$", StringComparison.Ordinal))
            .ToArray();
    }

    private static string[] DeclaredPublicProperties(Type type_)
    {
        return type_
            .GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly)
            .Select(property_ => property_.Name)
            .OrderBy(name_ => name_, StringComparer.Ordinal)
            .ToArray();
    }
}
