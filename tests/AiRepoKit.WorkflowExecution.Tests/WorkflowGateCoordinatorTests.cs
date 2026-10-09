namespace AiRepoKit.WorkflowExecution.Tests;

using System.Collections;
using System.Text;
using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Execution;
using AiRepoKit.Orchestration;
using AiRepoKit.WorkflowExecution;
using Xunit;

public sealed class WorkflowGateCoordinatorTests : IDisposable
{
    private const string ValidHash1 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ValidHash2 = "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";
    private const string ValidHash3 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ValidHash4 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string ValidHash5 = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private readonly string _tempDirectory;

    public WorkflowGateCoordinatorTests()
    {
        this._tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "repokit-p10-tests-" + Guid.NewGuid().ToString("N"));
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
                // Best effort test cleanup
            }
        }
    }

    // =========================================================================
    // Category 1: Surface & Construction
    // =========================================================================

    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                null!,
                [agent],
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullExecutors_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                store,
                null!,
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullValidators_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                null!,
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullRepairPolicy_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [],
                null!,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullReconciler_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [],
                policy,
                null!,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullGatePolicy_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [],
                policy,
                reconciler,
                null!,
                verifier));
    }

    [Fact]
    public void Constructor_NullVerifier_ThrowsArgumentNullException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);

        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [],
                policy,
                reconciler,
                gatePolicy,
                null!));
    }

    [Fact]
    public void Constructor_EmptyExecutors_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateCoordinator(
                store,
                [],
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullExecutorElement_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateCoordinator(
                store,
                [null!],
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullExecutorProviderId_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(null!, AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<InvalidOperationException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullExecutorCapabilities_ThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), null!);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<InvalidOperationException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_DuplicateExecutorProviderId_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent1 = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeAgent agent2 = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent1, agent2],
                [],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_NullValidatorElement_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [null!],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_UndefinedValidatorStrategy_ThrowsArgumentOutOfRangeException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator validator = new((ValidationStrategy) 9999);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [validator],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_DuplicateValidatorStrategy_ThrowsArgumentException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val1 = new(ValidationStrategy.Test);
        FakeValidator val2 = new(ValidationStrategy.Test);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateCoordinator(
                store,
                [agent],
                [val1, val2],
                policy,
                reconciler,
                gatePolicy,
                verifier));
    }

    [Fact]
    public void Constructor_EnumeratesExecutorsOnlyOnce()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        SingleEnumerationEnumerable<IAgentExecutor> enumerable = new(agent);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        WorkflowGateCoordinator coordinator = new(
            store,
            enumerable,
            [],
            policy,
            reconciler,
            gatePolicy,
            verifier);

        Assert.Equal(1, enumerable.EnumerationCount);
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Constructor_EnumeratesValidatorsOnlyOnce()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator validator = new(ValidationStrategy.Test);
        SingleEnumerationEnumerable<IValidationExecutor> valEnumerable = new(validator);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        WorkflowGateCoordinator coordinator = new(
            store,
            [agent],
            valEnumerable,
            policy,
            reconciler,
            gatePolicy,
            verifier);

        Assert.Equal(1, valEnumerable.EnumerationCount);
        Assert.NotNull(coordinator);
    }

    [Fact]
    public void Constructor_AllowsEmptyValidators()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowRepairPolicy policy = new(1, 1);
        FakeReconciler reconciler = new();
        WorkflowGatePolicy gatePolicy = new(false, true, true);
        FakeVerifier verifier = new();

        WorkflowGateCoordinator coordinator = new(
            store,
            [agent],
            [],
            policy,
            reconciler,
            gatePolicy,
            verifier);

        Assert.NotNull(coordinator);
    }

    // =========================================================================
    // Category 2: Policy
    // =========================================================================

    [Fact]
    public void Policy_RequiresGate_ReadOnly_True()
    {
        WorkflowGatePolicy policy = new(true, false, false);
        Assert.True(policy.RequiresGate(ExecutionPermission.ReadOnly));
    }

    [Fact]
    public void Policy_RequiresGate_ReadOnly_False()
    {
        WorkflowGatePolicy policy = new(false, true, true);
        Assert.False(policy.RequiresGate(ExecutionPermission.ReadOnly));
    }

    [Fact]
    public void Policy_RequiresGate_WorkspaceWrite_True()
    {
        WorkflowGatePolicy policy = new(false, true, false);
        Assert.True(policy.RequiresGate(ExecutionPermission.WorkspaceWrite));
    }

    [Fact]
    public void Policy_RequiresGate_WorkspaceWrite_False()
    {
        WorkflowGatePolicy policy = new(true, false, true);
        Assert.False(policy.RequiresGate(ExecutionPermission.WorkspaceWrite));
    }

    [Fact]
    public void Policy_RequiresGate_Unrestricted_True()
    {
        WorkflowGatePolicy policy = new(false, false, true);
        Assert.True(policy.RequiresGate(ExecutionPermission.Unrestricted));
    }

    [Fact]
    public void Policy_RequiresGate_Unrestricted_False()
    {
        WorkflowGatePolicy policy = new(true, true, false);
        Assert.False(policy.RequiresGate(ExecutionPermission.Unrestricted));
    }

    [Fact]
    public void Policy_RequiresGate_UndefinedPermission_Throws()
    {
        WorkflowGatePolicy policy = new(false, true, true);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            policy.RequiresGate((ExecutionPermission) 999));
    }

    [Fact]
    public void Policy_NoNumericRankingAssumption_ArbitraryCombinationWorks()
    {
        // ReadOnly required, WorkspaceWrite not required, Unrestricted required
        WorkflowGatePolicy policy = new(true, false, true);
        Assert.True(policy.RequiresGate(ExecutionPermission.ReadOnly));
        Assert.False(policy.RequiresGate(ExecutionPermission.WorkspaceWrite));
        Assert.True(policy.RequiresGate(ExecutionPermission.Unrestricted));
    }

    // =========================================================================
    // Category 3: WorkflowGateChallenge
    // =========================================================================

    [Fact]
    public void Challenge_InternalConstructor_ValidatesGateIdHex()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateChallenge("invalid-hash", wf, "t1", 1, 1, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnNullOrEmptyTaskId()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "", 1, 1, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnNonPositiveBasePersistenceRevision()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 0, 1, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnNonPositivePlanRevision()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 0, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnUndefinedRequiredPermission()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 1, (ExecutionPermission) 99, 1, ValidHash1, ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnInvalidGateOrdinal()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 1, ExecutionPermission.WorkspaceWrite, 2, ValidHash1, ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnInvalidBaseStateFingerprint()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 1, ExecutionPermission.WorkspaceWrite, 1, "short", ValidHash2, ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnInvalidInputFingerprint()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 1, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, "bad", ValidHash3, ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnInvalidRegistryFingerprint()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 1, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, ValidHash2, "bad", ValidHash4));
    }

    [Fact]
    public void Challenge_InternalConstructor_ThrowsOnInvalidPolicyFingerprint()
    {
        WorkflowId wf = new("wf-1");
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateChallenge(ValidHash1, wf, "t1", 1, 1, ExecutionPermission.WorkspaceWrite, 1, ValidHash1, ValidHash2, ValidHash3, "bad"));
    }

    [Fact]
    public void Challenge_InternalConstructor_SetsAllProperties()
    {
        WorkflowId wf = new("wf-1");
        WorkflowGateChallenge challenge = new(
            ValidHash1,
            wf,
            "task-1",
            3,
            2,
            ExecutionPermission.WorkspaceWrite,
            1,
            ValidHash2,
            ValidHash3,
            ValidHash4,
            ValidHash5);

        Assert.Equal(ValidHash1, challenge.GateId);
        Assert.Equal(wf, challenge.WorkflowId);
        Assert.Equal("task-1", challenge.TaskId);
        Assert.Equal(3, challenge.BasePersistenceRevision);
        Assert.Equal(2, challenge.SourceImplementationPlanRevision);
        Assert.Equal(ExecutionPermission.WorkspaceWrite, challenge.RequiredPermission);
        Assert.Equal(1, challenge.GateOrdinal);
        Assert.Equal(ValidHash2, challenge.BaseStateFingerprint);
        Assert.Equal(ValidHash3, challenge.InputFingerprint);
        Assert.Equal(ValidHash4, challenge.RegistryFingerprint);
        Assert.Equal(ValidHash5, challenge.PolicyFingerprint);
    }

    // =========================================================================
    // Category 4: WorkflowGateProof
    // =========================================================================

    [Fact]
    public void Proof_Constructor_ValidatesGateIdHex()
    {
        WorkflowGateProof proof = new(ValidHash1, "valid-evidence");
        Assert.Equal(ValidHash1, proof.GateId);
        Assert.Equal("valid-evidence", proof.OpaqueEvidence);
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnNullGateId()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateProof(null!, "evidence"));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnShortGateId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(new string('a', 63), "evidence"));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnLongGateId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(new string('a', 65), "evidence"));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnUppercaseGateId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(ValidHash1.ToUpperInvariant(), "evidence"));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnNonHexGateId()
    {
        string badHex = "g" + new string('a', 63);
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(badHex, "evidence"));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnNullOpaqueEvidence()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowGateProof(ValidHash1, null!));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnWhitespaceOpaqueEvidence()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(ValidHash1, "   \t\n"));
    }

    [Fact]
    public void Proof_Constructor_AllowsExactly65536BytesEvidence()
    {
        string bigEvidence = new('x', 65536);
        WorkflowGateProof proof = new(ValidHash1, bigEvidence);
        Assert.Equal(65536, Encoding.UTF8.GetByteCount(proof.OpaqueEvidence));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOn65537BytesEvidence()
    {
        string bigEvidence = new('x', 65537);
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(ValidHash1, bigEvidence));
    }

    [Fact]
    public void Proof_Constructor_ThrowsOnInvalidUnicodeInEvidence()
    {
        string invalidUnicode = "evidence\uD800"; // Unpaired high surrogate
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateProof(ValidHash1, invalidUnicode));
    }

    [Fact]
    public void Proof_Constructor_PreservesExactEvidenceWithoutNormalization()
    {
        string weirdEvidence = "  my\ttoken\r\nvalue  ";
        WorkflowGateProof proof = new(ValidHash1, weirdEvidence);
        Assert.Equal(weirdEvidence, proof.OpaqueEvidence);
    }

    // =========================================================================
    // Category 5: WorkflowGateVerificationResult
    // =========================================================================

    [Fact]
    public void VerificationResult_Authorized_ValidShape()
    {
        WorkflowGateVerificationResult result = new(
            isAuthenticated_: true,
            isAuthorized_: true,
            principalId_: "user-alice",
            mechanismId_: "cli-pin",
            evidenceFingerprint_: ValidHash1,
            failureReason_: null);

        Assert.True(result.IsAuthenticated);
        Assert.True(result.IsAuthorized);
        Assert.Equal("user-alice", result.PrincipalId);
        Assert.Equal("cli-pin", result.MechanismId);
        Assert.Equal(ValidHash1, result.EvidenceFingerprint);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public void VerificationResult_AuthenticatedDenial_ValidShape()
    {
        WorkflowGateVerificationResult result = new(
            isAuthenticated_: true,
            isAuthorized_: false,
            principalId_: "user-bob",
            mechanismId_: "cli-pin",
            evidenceFingerprint_: ValidHash1,
            failureReason_: "Operation rejected by policy.");

        Assert.True(result.IsAuthenticated);
        Assert.False(result.IsAuthorized);
        Assert.Equal("user-bob", result.PrincipalId);
        Assert.Equal("cli-pin", result.MechanismId);
        Assert.Equal(ValidHash1, result.EvidenceFingerprint);
        Assert.Equal("Operation rejected by policy.", result.FailureReason);
    }

    [Fact]
    public void VerificationResult_Unauthenticated_ValidShape()
    {
        WorkflowGateVerificationResult result = new(
            isAuthenticated_: false,
            isAuthorized_: false,
            principalId_: null,
            mechanismId_: null,
            evidenceFingerprint_: null,
            failureReason_: "Token expired or signature invalid.");

        Assert.False(result.IsAuthenticated);
        Assert.False(result.IsAuthorized);
        Assert.Null(result.PrincipalId);
        Assert.Null(result.MechanismId);
        Assert.Null(result.EvidenceFingerprint);
        Assert.Equal("Token expired or signature invalid.", result.FailureReason);
    }

    [Fact]
    public void VerificationResult_ThrowsOnUnauthenticatedWithAuthorizedTrue()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, true, null, null, null, "reason"));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithNullPrincipalId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, null, "mech", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithBlankPrincipalId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "   ", "mech", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithNullMechanismId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user-1", null, ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithBlankMechanismId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user-1", "  ", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithNullEvidenceFingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user-1", "mech", null, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithInvalidEvidenceFingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user-1", "mech", "not-hex-64", null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthorizedWithNonNullFailureReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user-1", "mech", ValidHash1, "some failure"));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthenticatedDenialWithNullFailureReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, false, "user-1", "mech", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnAuthenticatedDenialWithBlankFailureReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, false, "user-1", "mech", ValidHash1, "   "));
    }

    [Fact]
    public void VerificationResult_ThrowsOnUnauthenticatedWithNonNullPrincipalId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, "user-1", null, null, "failed"));
    }

    [Fact]
    public void VerificationResult_ThrowsOnUnauthenticatedWithNonNullMechanismId()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, null, "mech", null, "failed"));
    }

    [Fact]
    public void VerificationResult_ThrowsOnUnauthenticatedWithNonNullEvidenceFingerprint()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, null, null, ValidHash1, "failed"));
    }

    [Fact]
    public void VerificationResult_ThrowsOnUnauthenticatedWithNullFailureReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, null, null, null, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnUnauthenticatedWithBlankFailureReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, null, null, null, "   "));
    }

    [Fact]
    public void VerificationResult_ThrowsOnPrincipalExceeding256Bytes()
    {
        string longPrincipal = new('a', 257);
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, longPrincipal, "mech", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_AllowsPrincipalAt256Bytes()
    {
        string maxPrincipal = new('a', 256);
        WorkflowGateVerificationResult res = new(true, true, maxPrincipal, "mech", ValidHash1, null);
        Assert.Equal(maxPrincipal, res.PrincipalId);
    }

    [Fact]
    public void VerificationResult_ThrowsOnMechanismExceeding128Bytes()
    {
        string longMech = new('m', 129);
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user-1", longMech, ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_AllowsMechanismAt128Bytes()
    {
        string maxMech = new('m', 128);
        WorkflowGateVerificationResult res = new(true, true, "user-1", maxMech, ValidHash1, null);
        Assert.Equal(maxMech, res.MechanismId);
    }

    [Fact]
    public void VerificationResult_ThrowsOnFailureReasonExceeding1024Bytes()
    {
        string longReason = new('r', 1025);
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, null, null, null, longReason));
    }

    [Fact]
    public void VerificationResult_AllowsFailureReasonAt1024Bytes()
    {
        string maxReason = new('r', 1024);
        WorkflowGateVerificationResult res = new(false, false, null, null, null, maxReason);
        Assert.Equal(maxReason, res.FailureReason);
    }

    [Fact]
    public void VerificationResult_ThrowsOnInvalidUnicodeInPrincipal()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user\uD800", "mech", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnInvalidUnicodeInMechanism()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(true, true, "user", "mech\uD800", ValidHash1, null));
    }

    [Fact]
    public void VerificationResult_ThrowsOnInvalidUnicodeInFailureReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateVerificationResult(false, false, null, null, null, "reason\uD800"));
    }

    // =========================================================================
    // Category 6: WorkflowGateResult
    // =========================================================================

    [Fact]
    public void GateResult_Pending_ValidShape()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        WorkflowGateResult result = WorkflowGateResult.CreatePending(challenge, snapshot);

        Assert.Equal(WorkflowGateStatus.Pending, result.Status);
        Assert.Same(challenge, result.Challenge);
        Assert.Null(result.CheckpointResult);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void GateResult_Denied_ValidShape()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        WorkflowGateResult result = WorkflowGateResult.CreateDenied(challenge, snapshot);

        Assert.Equal(WorkflowGateStatus.Denied, result.Status);
        Assert.Same(challenge, result.Challenge);
        Assert.Null(result.CheckpointResult);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void GateResult_Approved_ValidShape()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot);

        WorkflowGateResult result = WorkflowGateResult.CreateApproved(challenge, checkpoint);

        Assert.Equal(WorkflowGateStatus.Approved, result.Status);
        Assert.Same(challenge, result.Challenge);
        Assert.Same(checkpoint, result.CheckpointResult);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void GateResult_Bypassed_ValidShape()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot);

        WorkflowGateResult result = WorkflowGateResult.CreateBypassed(checkpoint);

        Assert.Equal(WorkflowGateStatus.Bypassed, result.Status);
        Assert.Null(result.Challenge);
        Assert.Same(checkpoint, result.CheckpointResult);
        Assert.Same(snapshot, result.Snapshot);
    }

    [Fact]
    public void GateResult_Pending_ThrowsIfChallengeNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Pending, null, null, snapshot));
    }

    [Fact]
    public void GateResult_Pending_ThrowsIfCheckpointResultNotNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Pending, challenge, checkpoint, snapshot));
    }

    [Fact]
    public void GateResult_Denied_ThrowsIfChallengeNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Denied, null, null, snapshot));
    }

    [Fact]
    public void GateResult_Denied_ThrowsIfCheckpointResultNotNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Denied, challenge, checkpoint, snapshot));
    }

    [Fact]
    public void GateResult_Approved_ThrowsIfChallengeNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Approved, null, checkpoint, snapshot));
    }

    [Fact]
    public void GateResult_Approved_ThrowsIfCheckpointResultNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Approved, challenge, null, snapshot));
    }

    [Fact]
    public void GateResult_Approved_ThrowsIfSnapshotMismatch()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot1 = store.Load()!;

        WorkflowState blocked = WorkflowStateMachine.TransitionStep(snapshot1.State, "task-1", WorkflowStepStatus.Blocked);
        WorkflowPersistenceSnapshot snapshot2 = store.Append(blocked, snapshot1.Revision);
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot2);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Approved, challenge, checkpoint, snapshot1));
    }

    [Fact]
    public void GateResult_Bypassed_ThrowsIfChallengeNotNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Bypassed, challenge, checkpoint, snapshot));
    }

    [Fact]
    public void GateResult_Bypassed_ThrowsIfCheckpointResultNull()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Bypassed, null, null, snapshot));
    }

    [Fact]
    public void GateResult_Bypassed_ThrowsIfSnapshotMismatch()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot1 = store.Load()!;

        WorkflowState blocked = WorkflowStateMachine.TransitionStep(snapshot1.State, "task-1", WorkflowStepStatus.Blocked);
        WorkflowPersistenceSnapshot snapshot2 = store.Append(blocked, snapshot1.Revision);
        WorkflowCheckpointResult checkpoint = CreateCheckpointResult(snapshot2);

        Assert.Throws<ArgumentException>(() =>
            new WorkflowGateResult(WorkflowGateStatus.Bypassed, null, checkpoint, snapshot1));
    }

    [Fact]
    public void GateResult_ThrowsOnUndefinedStatus()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowPersistenceSnapshot snapshot = store.Load()!;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowGateResult((WorkflowGateStatus) 99, null, null, snapshot));
    }

    // =========================================================================
    // Category 7: Fingerprint Determinism
    // =========================================================================

    [Fact]
    public void ComputeBaseStateFingerprint_IsDeterministic()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowState state = store.Load()!.State;

        string fp1 = WorkflowGateCoordinator.ComputeBaseStateFingerprint(state);
        string fp2 = WorkflowGateCoordinator.ComputeBaseStateFingerprint(state);

        Assert.Equal(fp1, fp2);
        Assert.True(WorkflowGateChallenge.IsLowerHex64(fp1));
    }

    [Fact]
    public void ComputeBaseStateFingerprint_TaskOrderingIsDeterministic()
    {
        ExecutableWork work1 = new(1, [new ExecutableTask("task-a", "s1", "i1"), new ExecutableTask("task-b", "s2", "i2")], [], [], [], []);
        WorkflowState state1 = WorkflowStateMachine.Create(work1);
        ExecutableWork work2 = new(1, [new ExecutableTask("task-b", "s2", "i2"), new ExecutableTask("task-a", "s1", "i1")], [], [], [], []);
        WorkflowState state2 = WorkflowStateMachine.Create(work2);

        string fp1 = WorkflowGateCoordinator.ComputeBaseStateFingerprint(state1);
        string fp2 = WorkflowGateCoordinator.ComputeBaseStateFingerprint(state2);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputePolicyFingerprint_IsDeterministic()
    {
        WorkflowGatePolicy p = new(false, true, true);
        string fp1 = WorkflowGateCoordinator.ComputePolicyFingerprint(p);
        string fp2 = WorkflowGateCoordinator.ComputePolicyFingerprint(p);

        Assert.Equal(fp1, fp2);
        Assert.True(WorkflowGateChallenge.IsLowerHex64(fp1));
    }

    [Fact]
    public void ComputePolicyFingerprint_ChangesOnPolicyChange()
    {
        WorkflowGatePolicy p1 = new(false, true, true);
        WorkflowGatePolicy p2 = new(true, true, true);

        string fp1 = WorkflowGateCoordinator.ComputePolicyFingerprint(p1);
        string fp2 = WorkflowGateCoordinator.ComputePolicyFingerprint(p2);

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void ComputeRegistryFingerprint_IsDeterministic()
    {
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator val = new(ValidationStrategy.Test);

        string fp1 = WorkflowGateCoordinator.ComputeRegistryFingerprint([agent], [val]);
        string fp2 = WorkflowGateCoordinator.ComputeRegistryFingerprint([agent], [val]);

        Assert.Equal(fp1, fp2);
        Assert.True(WorkflowGateChallenge.IsLowerHex64(fp1));
    }

    [Fact]
    public void ComputeRegistryFingerprint_IndependentOfProviderOrder()
    {
        FakeAgent a1 = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeAgent a2 = new(new AgentProviderId("prov-2"), AgentCapabilitySet.Empty);

        string fp1 = WorkflowGateCoordinator.ComputeRegistryFingerprint([a1, a2], []);
        string fp2 = WorkflowGateCoordinator.ComputeRegistryFingerprint([a2, a1], []);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeRegistryFingerprint_IndependentOfCapabilityOrder()
    {
        AgentCapability c1 = new("cap-a");
        AgentCapability c2 = new("cap-b");

        FakeAgent a1 = new(new AgentProviderId("prov-1"), new AgentCapabilitySet([c1, c2]));
        FakeAgent a2 = new(new AgentProviderId("prov-1"), new AgentCapabilitySet([c2, c1]));

        string fp1 = WorkflowGateCoordinator.ComputeRegistryFingerprint([a1], []);
        string fp2 = WorkflowGateCoordinator.ComputeRegistryFingerprint([a2], []);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeGateId_IsDeterministic()
    {
        WorkflowId wf = new("wf-1");
        string gateId1 = WorkflowGateCoordinator.ComputeGateId(wf, "task-1", 2, 1, ExecutionPermission.WorkspaceWrite, ValidHash1, ValidHash2, ValidHash3, ValidHash4);
        string gateId2 = WorkflowGateCoordinator.ComputeGateId(wf, "task-1", 2, 1, ExecutionPermission.WorkspaceWrite, ValidHash1, ValidHash2, ValidHash3, ValidHash4);

        Assert.Equal(gateId1, gateId2);
        Assert.True(WorkflowGateChallenge.IsLowerHex64(gateId1));
    }

    [Fact]
    public void ComputeGateId_DoesNotDependOnClockOrGuid()
    {
        WorkflowId wf = new("wf-1");
        string gateId1 = WorkflowGateCoordinator.ComputeGateId(wf, "task-1", 2, 1, ExecutionPermission.WorkspaceWrite, ValidHash1, ValidHash2, ValidHash3, ValidHash4);
        Thread.Sleep(10);
        string gateId2 = WorkflowGateCoordinator.ComputeGateId(wf, "task-1", 2, 1, ExecutionPermission.WorkspaceWrite, ValidHash1, ValidHash2, ValidHash3, ValidHash4);

        Assert.Equal(gateId1, gateId2);
    }

    // =========================================================================
    // Category 8: Execution Preconditions
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_ThrowsOnNullWork()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) = this.CreateInitializedSetup();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.ExecuteAsync(null!, envelope));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOnNullEnvelope()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, _) = this.CreateInitializedSetup();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            coordinator.ExecuteAsync(work, null!));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsIfPersistenceStoreNotInitialized()
    {
        WorkflowId wf = new("wf-uninit");
        WorkflowPersistenceStore uninitStore = new(this._tempDirectory, wf);
        ExecutableWork work = CreateWork();
        ExecutionEnvelope envelope = Envelope(work);
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(uninitStore);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsOnPlanRevisionMismatch()
    {
        (WorkflowPersistenceStore store, _, ExecutionEnvelope envelope) = this.CreateInitializedSetup(revision: 1);
        ExecutableWork mismatchedWork = CreateWork(revision: 2);
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(mismatchedWork, envelope));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsIfTaskNotInWork()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, _) = this.CreateInitializedSetup(taskId: "task-1");
        ExecutableWork otherWork = CreateWork(taskId: "other-task");
        ExecutionEnvelope badEnvelope = Envelope(otherWork);
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.ExecuteAsync(work, badEnvelope));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsIfTaskNotInState()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup(taskId: "task-1");
        ExecutableWork workWithTwo = new(
            1,
            [
                new ExecutableTask("task-1", "s1", "i1"),
                new ExecutableTask("task-2", "s2", "i2")
            ],
            [],
            [],
            [],
            []);

        ExecutionEnvelope envForTask2 = ExecutionEnvelope.Create(
            workWithTwo,
            new CompiledPrompt("task-2", "instruction"),
            ExecutionPermission.WorkspaceWrite,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.ExecuteAsync(workWithTwo, envForTask2));
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsIfSessionReferenceNotNull()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, _) = this.CreateInitializedSetup();
        ExecutionEnvelope envWithSession = ExecutionEnvelope.Create(
            work,
            new CompiledPrompt("task-1", "instruction"),
            ExecutionPermission.WorkspaceWrite,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: new AgentSessionReference("existing-session"),
            structuredOutput_: null,
            timeout_: null);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envWithSession));
    }

    [Fact]
    public async Task ExecuteAsync_RespectsCancellationTokenBeforeExecution()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) = this.CreateInitializedSetup();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coordinator.ExecuteAsync(work, envelope, cancellationToken_: cts.Token));
    }

    // =========================================================================
    // Category 9: Policy Bypass
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_PolicyBypass_DelegatesToP09Directly()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.ReadOnly);

        // Policy does not require gate for ReadOnly
        WorkflowGatePolicy policy = new(false, true, true);
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeVerifier verifier = new();

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, gatePolicy: policy, verifier: verifier);

        WorkflowGateResult result = await coordinator.ExecuteAsync(work, envelope);

        Assert.Equal(WorkflowGateStatus.Bypassed, result.Status);
        Assert.Null(result.Challenge);
        Assert.NotNull(result.CheckpointResult);
        Assert.Equal(0, verifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_PolicyBypass_ThrowsIfProofProvided()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.ReadOnly);

        WorkflowGatePolicy policy = new(false, true, true);
        FakeVerifier verifier = new();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, gatePolicy: policy, verifier: verifier);

        WorkflowGateProof proof = new(ValidHash1, "unsolicited-evidence");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof));

        Assert.Equal(0, verifier.InvocationCount);
    }

    // =========================================================================
    // Category 10: Gated Lifecycle (Pending, Verify, Denial, Approved)
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_RequiresGate_WithoutProof_ReturnsPendingAndStepIsBlocked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeVerifier verifier = new();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, verifier: verifier);

        WorkflowGateResult result = await coordinator.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(WorkflowGateStatus.Pending, result.Status);
        Assert.NotNull(result.Challenge);
        Assert.Null(result.CheckpointResult);
        Assert.Equal(0, verifier.InvocationCount);
        Assert.Equal(0, agent.InvocationCount);

        // Canonical step transitioned to Blocked
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, snapshot.State.Steps[0].Status);
        Assert.Equal(4, snapshot.Revision); // 1=init, 2=running-wf, 3=running-step, 4=blocked-step
    }

    [Fact]
    public async Task ExecuteAsync_RequiresGate_WithProof_MismatchedGateId_ThrowsBeforeVerifier()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        // Provide proof with wrong gateId
        WorkflowGateProof wrongProof = new(ValidHash2, "some-proof");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.ExecuteAsync(work, envelope, wrongProof));

        Assert.Equal(0, verifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresGate_WithProof_Unauthenticated_ThrowsAndStepRemainsBlocked()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(false, false, null, null, null, "invalid credentials")));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        // First call without proof to create the gate
        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pendingResult.Challenge!.GateId;

        WorkflowGateProof proof = new(gateId, "bad-evidence");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof));

        // Step remains blocked, zero decision record
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, snapshot.State.Steps[0].Status);

        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pendingResult.Challenge!.BasePersistenceRevision);
        Assert.Single(journal.Records);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresGate_WithProof_AuthenticatedDenial_WritesRecordAndReturnsDenied()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, false, "alice", "pin", ValidHash3, "Human rejected step.")));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pendingResult.Challenge!.GateId;

        WorkflowGateProof proof = new(gateId, "rejected-evidence");

        WorkflowGateResult deniedResult = await coordinator.ExecuteAsync(work, envelope, proof);

        Assert.Equal(WorkflowGateStatus.Denied, deniedResult.Status);
        Assert.Equal(gateId, deniedResult.Challenge!.GateId);
        Assert.Null(deniedResult.CheckpointResult);

        // Step remains blocked
        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, snapshot.State.Steps[0].Status);

        // Journal has Denial record (sequence 2)
        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pendingResult.Challenge!.BasePersistenceRevision);
        Assert.Equal(2, journal.Records.Count);
        Assert.NotNull(journal.DeniedRecord);
        Assert.Equal("alice", journal.DeniedRecord.PrincipalId);
        Assert.Equal("pin", journal.DeniedRecord.MechanismId);
        Assert.Equal(ValidHash3, journal.DeniedRecord.EvidenceFingerprint);
    }

    [Fact]
    public async Task ExecuteAsync_RequiresGate_WithProof_AuthenticatedApproval_UnblocksRunsP09AndReturnsApproved()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "alice", "pin", ValidHash3, null)));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, verifier: verifier);

        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pendingResult.Challenge!.GateId;

        WorkflowGateProof proof = new(gateId, "approved-evidence");

        WorkflowGateResult approvedResult = await coordinator.ExecuteAsync(work, envelope, proof);

        Assert.Equal(WorkflowGateStatus.Approved, approvedResult.Status);
        Assert.Equal(gateId, approvedResult.Challenge!.GateId);
        Assert.NotNull(approvedResult.CheckpointResult);
        Assert.Equal(1, agent.InvocationCount);

        // Journal has Grant record (sequence 2)
        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pendingResult.Challenge!.BasePersistenceRevision);
        Assert.Equal(2, journal.Records.Count);
        Assert.NotNull(journal.GrantedRecord);
        Assert.Equal("alice", journal.GrantedRecord.PrincipalId);
    }

    [Fact]
    public async Task ExecuteAsync_NullVerifierResult_ThrowsFailClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new(handler_: (ch, pr, ct) => Task.FromResult<WorkflowGateVerificationResult>(null!));
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pendingResult.Challenge!.GateId;

        WorkflowGateProof proof = new(gateId, "some-evidence");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof));

        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, snapshot.State.Steps[0].Status);
    }

    [Fact]
    public async Task ExecuteAsync_VerifierException_PropagatesAndStepRemainsBlockedWithoutDecisionRecord()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new(handler_: (ch, pr, ct) => throw new ApplicationException("Verifier crashed!"));
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pendingResult.Challenge!.GateId;

        WorkflowGateProof proof = new(gateId, "evidence");

        await Assert.ThrowsAsync<ApplicationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof));

        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, snapshot.State.Steps[0].Status);

        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pendingResult.Challenge!.BasePersistenceRevision);
        Assert.Single(journal.Records);
    }

    [Fact]
    public async Task ExecuteAsync_VerifierCancellation_PropagatesAndStepRemainsBlockedWithoutDecisionRecord()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new(handler_: (ch, pr, ct) => throw new OperationCanceledException(ct));
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pendingResult.Challenge!.GateId;

        WorkflowGateProof proof = new(gateId, "evidence");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof));

        WorkflowPersistenceSnapshot snapshot = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, snapshot.State.Steps[0].Status);

        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pendingResult.Challenge!.BasePersistenceRevision);
        Assert.Single(journal.Records);
    }

    [Fact]
    public async Task ExecuteAsync_ZeroP09InvocationBeforeDurableGrant()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent);

        await coordinator.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(0, agent.InvocationCount);
    }

    // =========================================================================
    // Category 11: Journal & Path Encoding
    // =========================================================================

    [Fact]
    public void Journal_PathMatchesExactCanonicalFormat()
    {
        WorkflowId wf = new("my-workflow");
        string path = WorkflowGateJournal.GetRecordsDirectory(this._tempDirectory, wf, "my-task", 5);

        string expectedWfHex = Convert.ToHexString(Encoding.UTF8.GetBytes("my-workflow")).ToLowerInvariant();
        string expectedTaskHex = Convert.ToHexString(Encoding.UTF8.GetBytes("my-task")).ToLowerInvariant();

        Assert.Contains(Path.Combine("workflows", expectedWfHex, "human-gates", expectedTaskHex, "00000000000000000005", "records"), path);
    }

    [Fact]
    public void Journal_RecordSequenceEnforcesMaximumTwoRecords()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);

        AuthorizationGrantedRecord grant = new()
        {
            Sequence = 2,
            GateId = challenge.GateId,
            PrincipalId = "alice",
            MechanismId = "pin",
            EvidenceFingerprint = ValidHash1
        };
        journal.AppendRecord(grant);

        AuthorizationDeniedRecord thirdRecord = new()
        {
            Sequence = 3,
            GateId = challenge.GateId,
            PrincipalId = "bob",
            MechanismId = "pin",
            EvidenceFingerprint = ValidHash2
        };

        Assert.Throws<InvalidOperationException>(() =>
            journal.AppendRecord(thirdRecord));
    }

    [Fact]
    public void Journal_DoesNotPersistOpaqueEvidenceOrFailureReason()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);

        AuthorizationDeniedRecord denial = new()
        {
            Sequence = 2,
            GateId = challenge.GateId,
            PrincipalId = "bob",
            MechanismId = "pin",
            EvidenceFingerprint = ValidHash2
        };
        journal.AppendRecord(denial);

        string record2File = Path.Combine(journal.RecordsDirectory, "00000000000000000002.json");
        string json = File.ReadAllText(record2File);

        Assert.DoesNotContain("failureReason", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opaqueEvidence", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Journal_AppendCollisionThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);

        // Pre-create file 00000000000000000002.json to simulate write collision
        string collisionFile = Path.Combine(journal.RecordsDirectory, "00000000000000000002.json");
        File.WriteAllText(collisionFile, "{}");

        AuthorizationGrantedRecord grant = new()
        {
            Sequence = 2,
            GateId = challenge.GateId,
            PrincipalId = "alice",
            MechanismId = "pin",
            EvidenceFingerprint = ValidHash1
        };

        Assert.Throws<InvalidOperationException>(() =>
            journal.AppendRecord(grant));
    }

    [Fact]
    public void Journal_CorruptRecordFileThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string record1File = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        File.WriteAllText(record1File, "{ corrupted json... }");

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    [Fact]
    public void Journal_UnknownSchemaIdThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string record1File = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string json = File.ReadAllText(record1File).Replace("ai.repokit.workflow-gate-journal-record", "unknown-schema");
        File.WriteAllText(record1File, json);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    [Fact]
    public void Journal_UnknownSchemaVersionThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string record1File = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string json = File.ReadAllText(record1File).Replace("\"schemaVersion\":1", "\"schemaVersion\":99");
        File.WriteAllText(record1File, json);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    [Fact]
    public void Journal_UnknownRecordKindThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string record1File = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string json = File.ReadAllText(record1File).Replace("ChallengeCreated", "UnknownKind");
        File.WriteAllText(record1File, json);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    [Fact]
    public void Journal_SequenceGapThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string record1File = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        string record3File = Path.Combine(journal.RecordsDirectory, "00000000000000000003.json");
        File.Move(record1File, record3File);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    [Fact]
    public void Journal_Utf8BomThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string record1File = Path.Combine(journal.RecordsDirectory, "00000000000000000001.json");
        byte[] bytes = File.ReadAllBytes(record1File);
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. bytes];
        File.WriteAllBytes(record1File, withBom);

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    [Fact]
    public void Journal_NonCanonicalFileNameThrowsInvalidOperationException()
    {
        (WorkflowPersistenceStore store, _, _) = this.CreateInitializedSetup();
        WorkflowGateChallenge challenge = CreateDummyChallenge();

        WorkflowGateJournal journal = WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, "task-1", 2, challenge);
        string badFile = Path.Combine(journal.RecordsDirectory, "record-1.json");
        File.WriteAllText(badFile, "{}");

        Assert.Throws<InvalidOperationException>(() =>
            WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, "task-1", 2));
    }

    // =========================================================================
    // Category 12: Drift & Conflict
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_DriftInInputFingerprint_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);
        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        long baseRev = pendingResult.Challenge!.BasePersistenceRevision;

        // Tamper with journal input fingerprint
        string dir = WorkflowGateJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, envelope.TaskId, baseRev);
        string file = Path.Combine(dir, "00000000000000000001.json");
        string json = File.ReadAllText(file).Replace(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(file)).GetProperty("inputFingerprint").GetString()!,
            ValidHash5);
        File.WriteAllText(file, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    [Fact]
    public async Task ExecuteAsync_DriftInRegistryFingerprint_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);
        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        long baseRev = pendingResult.Challenge!.BasePersistenceRevision;

        string dir = WorkflowGateJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, envelope.TaskId, baseRev);
        string file = Path.Combine(dir, "00000000000000000001.json");
        string json = File.ReadAllText(file).Replace(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(file)).GetProperty("registryFingerprint").GetString()!,
            ValidHash5);
        File.WriteAllText(file, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    [Fact]
    public async Task ExecuteAsync_DriftInPolicyFingerprint_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);
        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        long baseRev = pendingResult.Challenge!.BasePersistenceRevision;

        string dir = WorkflowGateJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, envelope.TaskId, baseRev);
        string file = Path.Combine(dir, "00000000000000000001.json");
        string json = File.ReadAllText(file).Replace(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(file)).GetProperty("policyFingerprint").GetString()!,
            ValidHash5);
        File.WriteAllText(file, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    [Fact]
    public async Task ExecuteAsync_DriftInBaseStateFingerprint_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);
        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        long baseRev = pendingResult.Challenge!.BasePersistenceRevision;

        string dir = WorkflowGateJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, envelope.TaskId, baseRev);
        string file = Path.Combine(dir, "00000000000000000001.json");
        string json = File.ReadAllText(file).Replace(
            JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(file)).GetProperty("baseStateFingerprint").GetString()!,
            ValidHash5);
        File.WriteAllText(file, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    [Fact]
    public async Task ExecuteAsync_DriftInRequiredPermission_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);
        WorkflowGateResult pendingResult = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        long baseRev = pendingResult.Challenge!.BasePersistenceRevision;

        string dir = WorkflowGateJournal.GetRecordsDirectory(store.StorageRoot, store.WorkflowId, envelope.TaskId, baseRev);
        string file = Path.Combine(dir, "00000000000000000001.json");
        string json = File.ReadAllText(file).Replace("\"requiredPermission\":2", "\"requiredPermission\":3");
        File.WriteAllText(file, json);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    [Fact]
    public async Task ExecuteAsync_CandidateRevisionExceedsCanonicalRevision_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        // Pre-create future journal at revision 99
        WorkflowGateChallenge challenge = CreateDummyChallenge(baseRev: 99);
        WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, envelope.TaskId, 99, challenge);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    [Fact]
    public async Task ExecuteAsync_TargetStepAlreadyBlockedWithoutGate_FailsClosed()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        // Transition step to Blocked directly in canonical store without creating a gate journal
        long currentRev = store.Load()!.Revision;
        WorkflowState blocked = WorkflowStateMachine.TransitionStep(store.Load()!.State, envelope.TaskId, WorkflowStepStatus.Blocked);
        store.Append(blocked, currentRev);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(work, envelope, proof_: null));
    }

    // =========================================================================
    // Category 13: Restart Windows & Recovery
    // =========================================================================

    [Fact]
    public async Task Recovery_AfterChallengeBeforeBlock_RecoversAndBlocks()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        // Simulate crash right after challenge creation before canonical block:
        // Journal exists at base revision, but store is still at base revision (step is Running)
        long baseRev = store.Load()!.Revision;
        WorkflowState baseState = store.Load()!.State;
        string baseFp = WorkflowGateCoordinator.ComputeBaseStateFingerprint(baseState);
        string inputFp = WorkflowCheckpointCoordinatorContext.ComputeInputFingerprint(work, envelope, new WorkflowRepairPolicy(1, 1));
        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator validator = new(ValidationStrategy.Test);
        string regFp = WorkflowGateCoordinator.ComputeRegistryFingerprint([agent], [validator]);
        WorkflowGatePolicy policy = new(false, true, true);
        string polFp = WorkflowGateCoordinator.ComputePolicyFingerprint(policy);
        string gateId = WorkflowGateCoordinator.ComputeGateId(store.WorkflowId, envelope.TaskId, baseRev, 1, ExecutionPermission.WorkspaceWrite, baseFp, inputFp, regFp, polFp);

        WorkflowGateChallenge challenge = new(gateId, store.WorkflowId, envelope.TaskId, baseRev, 1, ExecutionPermission.WorkspaceWrite, 1, baseFp, inputFp, regFp, polFp);
        WorkflowGateJournal.CreateNew(store.StorageRoot, store.WorkflowId, envelope.TaskId, baseRev, challenge);

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, validator: validator, gatePolicy: policy);

        // Resume call
        WorkflowGateResult result = await coordinator.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(WorkflowGateStatus.Pending, result.Status);
        Assert.Equal(gateId, result.Challenge!.GateId);

        // Step was cleanly transitioned to Blocked
        WorkflowPersistenceSnapshot current = store.Load()!;
        Assert.Equal(WorkflowStepStatus.Blocked, current.State.Steps[0].Status);
        Assert.Equal(baseRev + 1, current.Revision);
    }

    [Fact]
    public async Task Recovery_AfterBlockBeforeProof_ReturnsPendingWithoutReverifying()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new();
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);

        // First execution creates challenge and blocks
        WorkflowGateResult r1 = await coordinator.ExecuteAsync(work, envelope, proof_: null);

        // Second execution after restart (proof still null)
        WorkflowGateCoordinator restarted = this.CreateCoordinator(store, verifier: verifier);
        WorkflowGateResult r2 = await restarted.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(WorkflowGateStatus.Pending, r2.Status);
        Assert.Equal(r1.Challenge!.GateId, r2.Challenge!.GateId);
        Assert.Equal(0, verifier.InvocationCount);
    }

    [Fact]
    public async Task Recovery_CrashDuringVerifier_ResubmissionRequiredNoImplicitApproval()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier crashingVerifier = new(handler_: (ch, pr, ct) => throw new ApplicationException("Verifier crash"));
        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: crashingVerifier);

        WorkflowGateResult pending = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pending.Challenge!.GateId;

        await Assert.ThrowsAsync<ApplicationException>(() =>
            coordinator.ExecuteAsync(work, envelope, new WorkflowGateProof(gateId, "proof")));

        // After crash: coordinator requires resubmission, no implicit approval
        FakeVerifier goodVerifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "alice", "pin", ValidHash1, null)));

        WorkflowGateCoordinator restarted = this.CreateCoordinator(store, verifier: goodVerifier);
        WorkflowGateResult resubmitted = await restarted.ExecuteAsync(work, envelope, new WorkflowGateProof(gateId, "proof"));

        Assert.Equal(WorkflowGateStatus.Approved, resubmitted.Status);
    }

    [Fact]
    public async Task Recovery_CrashAfterVerificationBeforeGrantRecord_RequiresResubmission()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "alice", "pin", ValidHash1, null)));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: verifier);
        WorkflowGateResult pending = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pending.Challenge!.GateId;

        // Step is Blocked, journal has 1 record.
        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pending.Challenge!.BasePersistenceRevision);
        Assert.Single(journal.Records);

        // Verification must be performed on next submission
        WorkflowGateResult approved = await coordinator.ExecuteAsync(work, envelope, new WorkflowGateProof(gateId, "proof"));
        Assert.Equal(WorkflowGateStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task Recovery_AfterGrantBeforeUnblock_AppendsBlockedToRunningAndDelegatesToP09()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "alice", "pin", ValidHash1, null)));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, verifier: verifier);
        WorkflowGateResult pending = await coordinator.ExecuteAsync(work, envelope, proof_: null);

        // Manually write grant record into journal, simulating crash right before unblock:
        // Canonical state is at revision Blocked. Journal has sequence 2 (Grant).
        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pending.Challenge!.BasePersistenceRevision);
        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            Sequence = 2,
            GateId = pending.Challenge!.GateId,
            PrincipalId = "alice",
            MechanismId = "pin",
            EvidenceFingerprint = ValidHash1
        });

        FakeVerifier restartedVerifier = new();
        WorkflowGateCoordinator restarted = this.CreateCoordinator(store, agent: agent, verifier: restartedVerifier);

        // Execute recovery without proof
        WorkflowGateResult recoveredResult = await restarted.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(WorkflowGateStatus.Approved, recoveredResult.Status);
        Assert.NotNull(recoveredResult.CheckpointResult);
        Assert.Equal(1, agent.InvocationCount);
        Assert.Equal(0, restartedVerifier.InvocationCount); // Did not reverify!
    }

    [Fact]
    public async Task Recovery_AfterUnblockBeforeP09_DelegatesToP09WithoutReverifying()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "alice", "pin", ValidHash1, null)));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, verifier: verifier);
        WorkflowGateResult pending = await coordinator.ExecuteAsync(work, envelope, proof_: null);

        // Write grant record
        WorkflowGateJournal journal = WorkflowGateJournal.OpenExisting(store.StorageRoot, store.WorkflowId, envelope.TaskId, pending.Challenge!.BasePersistenceRevision);
        journal.AppendRecord(new AuthorizationGrantedRecord
        {
            Sequence = 2,
            GateId = pending.Challenge!.GateId,
            PrincipalId = "alice",
            MechanismId = "pin",
            EvidenceFingerprint = ValidHash1
        });

        // Write canonical Blocked -> Running
        WorkflowState running = WorkflowStateMachine.TransitionStep(store.Load()!.State, envelope.TaskId, WorkflowStepStatus.Running);
        store.Append(running, store.Load()!.Revision);

        FakeVerifier restartedVerifier = new();
        WorkflowGateCoordinator restarted = this.CreateCoordinator(store, agent: agent, verifier: restartedVerifier);

        WorkflowGateResult recoveredResult = await restarted.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(WorkflowGateStatus.Approved, recoveredResult.Status);
        Assert.NotNull(recoveredResult.CheckpointResult);
        Assert.Equal(1, agent.InvocationCount);
        Assert.Equal(0, restartedVerifier.InvocationCount);
    }

    [Fact]
    public async Task Recovery_DurableDenial_Terminal_NewProofCannotOverride()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeVerifier denyingVerifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, false, "alice", "pin", ValidHash1, "Denied.")));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, verifier: denyingVerifier);
        WorkflowGateResult pending = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        string gateId = pending.Challenge!.GateId;

        WorkflowGateResult denied = await coordinator.ExecuteAsync(work, envelope, new WorkflowGateProof(gateId, "evidence"));
        Assert.Equal(WorkflowGateStatus.Denied, denied.Status);

        // Caller submits a "valid" proof later
        FakeVerifier approvingVerifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "bob", "pin", ValidHash2, null)));

        WorkflowGateCoordinator restarted = this.CreateCoordinator(store, verifier: approvingVerifier);
        WorkflowGateResult terminalDenied = await restarted.ExecuteAsync(work, envelope, new WorkflowGateProof(gateId, "new-evidence"));

        Assert.Equal(WorkflowGateStatus.Denied, terminalDenied.Status);
        Assert.Equal(0, approvingVerifier.InvocationCount); // Zero verifier call
    }

    [Fact]
    public async Task Recovery_P09ReplayAfterGate_ReturnsApprovedWithReplayedSnapshot()
    {
        (WorkflowPersistenceStore store, ExecutableWork work, ExecutionEnvelope envelope) =
            this.CreateInitializedSetup(permission: ExecutionPermission.WorkspaceWrite);

        FakeAgent agent = new(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeVerifier verifier = new(handler_: (ch, pr, ct) =>
            Task.FromResult(new WorkflowGateVerificationResult(true, true, "alice", "pin", ValidHash1, null)));

        WorkflowGateCoordinator coordinator = this.CreateCoordinator(store, agent: agent, verifier: verifier);

        // First full run to completion
        WorkflowGateResult pending = await coordinator.ExecuteAsync(work, envelope, proof_: null);
        WorkflowGateResult approved = await coordinator.ExecuteAsync(work, envelope, new WorkflowGateProof(pending.Challenge!.GateId, "proof"));
        Assert.Equal(WorkflowGateStatus.Approved, approved.Status);
        Assert.Equal(1, agent.InvocationCount);

        // Second run replaying P09
        WorkflowGateCoordinator replayCoordinator = this.CreateCoordinator(store, agent: agent, verifier: verifier);
        WorkflowGateResult replayResult = await replayCoordinator.ExecuteAsync(work, envelope, proof_: null);

        Assert.Equal(WorkflowGateStatus.Approved, replayResult.Status);
        Assert.Equal(1, agent.InvocationCount); // Agent not invoked again on completed replay
    }

    // =========================================================================
    // Helpers & Fakes
    // =========================================================================

    private WorkflowGateCoordinator CreateCoordinator(
        WorkflowPersistenceStore store_,
        FakeAgent? agent = null,
        FakeValidator? validator = null,
        WorkflowRepairPolicy? repairPolicy = null,
        FakeReconciler? reconciler = null,
        WorkflowGatePolicy? gatePolicy = null,
        FakeVerifier? verifier = null)
    {
        FakeAgent actAgent = agent ?? new FakeAgent(new AgentProviderId("prov-1"), AgentCapabilitySet.Empty);
        FakeValidator actVal = validator ?? new FakeValidator(ValidationStrategy.Test);
        WorkflowRepairPolicy actPolicy = repairPolicy ?? new WorkflowRepairPolicy(1, 1);
        FakeReconciler actReconciler = reconciler ?? new FakeReconciler();
        WorkflowGatePolicy actGatePolicy = gatePolicy ?? new WorkflowGatePolicy(false, true, true);
        FakeVerifier actVerifier = verifier ?? new FakeVerifier();

        return new WorkflowGateCoordinator(
            store_,
            [actAgent],
            [actVal],
            actPolicy,
            actReconciler,
            actGatePolicy,
            actVerifier);
    }

    private WorkflowGateChallenge CreateDummyChallenge(long baseRev = 2)
    {
        return new WorkflowGateChallenge(
            ValidHash1,
            new WorkflowId("wf-1"),
            "task-1",
            baseRev,
            1,
            ExecutionPermission.WorkspaceWrite,
            1,
            ValidHash2,
            ValidHash3,
            ValidHash4,
            ValidHash5);
    }

    private static WorkflowCheckpointResult CreateCheckpointResult(
        WorkflowPersistenceSnapshot snapshot_)
    {
        return new WorkflowCheckpointResult(
            snapshot_,
            new WorkflowRepairResult(snapshot_.State, [], false),
            resumed_: false,
            reconciliationUsed_: false);
    }

    private (WorkflowPersistenceStore Store, ExecutableWork Work, ExecutionEnvelope Envelope) CreateInitializedSetup(
        string taskId = "task-1",
        int revision = 1,
        ExecutionPermission permission = ExecutionPermission.WorkspaceWrite)
    {
        WorkflowId workflowId = new("wf-1");
        ExecutableWork work = CreateWork(taskId, revision);
        WorkflowPersistenceStore store = new(this._tempDirectory, workflowId);

        WorkflowState created = WorkflowStateMachine.Create(work);
        store.Initialize(created);

        WorkflowState runningWorkflow = WorkflowStateMachine.TransitionWorkflow(created, WorkflowStatus.Running);
        store.Append(runningWorkflow, 1);

        WorkflowState runningStep = WorkflowStateMachine.TransitionStep(runningWorkflow, taskId, WorkflowStepStatus.Running);
        store.Append(runningStep, 2);

        ExecutionEnvelope envelope = Envelope(work, permission);

        return (store, work, envelope);
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
            [
                new ValidationRequirement(
                    "val-1",
                    taskId,
                    "crit-1",
                    ValidationStrategy.Test,
                    "statement")
            ],
            [],
            []);
    }

    private static ExecutionEnvelope Envelope(
        ExecutableWork work_,
        ExecutionPermission permission_ = ExecutionPermission.WorkspaceWrite)
    {
        CompiledPrompt prompt = new(
            work_.Tasks[0].Id,
            "instruction");

        return ExecutionEnvelope.Create(
            work_,
            prompt,
            permission_,
            new ExecutionEnvironment(Path.GetFullPath(".")),
            sessionReference_: null,
            structuredOutput_: null,
            timeout_: null);
    }

    private sealed class FakeVerifier : IWorkflowGateVerifier
    {
        private readonly Func<WorkflowGateChallenge, WorkflowGateProof, CancellationToken, Task<WorkflowGateVerificationResult>> _handler;

        public int InvocationCount
        {
            get;
            private set;
        }

        public FakeVerifier(
            Func<WorkflowGateChallenge, WorkflowGateProof, CancellationToken, Task<WorkflowGateVerificationResult>>? handler_ = null)
        {
            this._handler = handler_ ?? ((ch, pr, ct) =>
                Task.FromResult(new WorkflowGateVerificationResult(true, true, "principal-1", "mech-1", ValidHash1, null)));
        }

        public async Task<WorkflowGateVerificationResult> VerifyAsync(
            WorkflowGateChallenge challenge_,
            WorkflowGateProof proof_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;
            return await this._handler(challenge_, proof_, cancellationToken_).ConfigureAwait(false);
        }
    }

    private sealed class FakeAgent : IAgentExecutor
    {
        private readonly Func<int, AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>> _handler;

        public AgentProviderId ProviderId
        {
            get;
        }

        public AgentCapabilitySet Capabilities
        {
            get;
        }

        public int InvocationCount
        {
            get;
            private set;
        }

        public FakeAgent(
            AgentProviderId providerId_,
            AgentCapabilitySet capabilities_,
            Func<int, AgentExecutionRequest, CancellationToken, Task<AgentExecutionResult>>? handler_ = null)
        {
            this.ProviderId = providerId_;
            this.Capabilities = capabilities_;
            this._handler = handler_ ?? ((_, _, _) => Task.FromResult(AgentExecutionResult.Completed()));
        }

        public async Task<AgentExecutionResult> ExecuteAsync(
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;
            return await this._handler(this.InvocationCount, request_, cancellationToken_).ConfigureAwait(false);
        }
    }

    private sealed class FakeValidator : IValidationExecutor
    {
        private readonly Func<int, ValidationRequirement, ExecutionEnvironment, CancellationToken, Task<ValidationExecutionResult>> _handler;

        public ValidationStrategy Strategy
        {
            get;
        }

        public int InvocationCount
        {
            get;
            private set;
        }

        public FakeValidator(
            ValidationStrategy strategy_,
            Func<int, ValidationRequirement, ExecutionEnvironment, CancellationToken, Task<ValidationExecutionResult>>? handler_ = null)
        {
            this.Strategy = strategy_;
            this._handler = handler_ ?? ((_, _, _, _) => Task.FromResult(new ValidationExecutionResult(true, "ok")));
        }

        public async Task<ValidationExecutionResult> ValidateAsync(
            ValidationRequirement requirement_,
            ExecutionEnvironment environment_,
            CancellationToken cancellationToken_ = default)
        {
            this.InvocationCount++;
            return await this._handler(this.InvocationCount, requirement_, environment_, cancellationToken_).ConfigureAwait(false);
        }
    }

    private sealed class FakeReconciler : IWorkflowSideEffectReconciler
    {
        public Task<WorkflowSideEffectReconciliationResult> ReconcileAgentAsync(
            WorkflowId workflowId_,
            string taskId_,
            string operationId_,
            long operationOrdinal_,
            int invocationGeneration_,
            AgentProviderId providerId_,
            AgentExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            return Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
        }

        public Task<WorkflowSideEffectReconciliationResult> ReconcileValidationAsync(
            WorkflowId workflowId_,
            string taskId_,
            string operationId_,
            long operationOrdinal_,
            int invocationGeneration_,
            ValidationRequirement requirement_,
            ExecutionEnvironment environment_,
            CancellationToken cancellationToken_ = default)
        {
            return Task.FromResult(new WorkflowSideEffectReconciliationResult(true, null, null));
        }
    }

    private sealed class SingleEnumerationEnumerable<T> : IEnumerable<T>
    {
        private readonly T[] _items;

        public int EnumerationCount
        {
            get;
            private set;
        }

        public SingleEnumerationEnumerable(params T[] items_)
        {
            this._items = items_;
        }

        public IEnumerator<T> GetEnumerator()
        {
            this.EnumerationCount++;
            if (this.EnumerationCount > 1)
            {
                throw new InvalidOperationException("Collection was enumerated more than once.");
            }
            return ((IEnumerable<T>) this._items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    }
}
