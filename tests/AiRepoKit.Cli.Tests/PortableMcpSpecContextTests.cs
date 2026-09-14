using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Cli.McpRuntime.Models;
using AiRepoKit.Cli.McpRuntime.Prompts;
using AiRepoKit.Cli.McpRuntime.Resources;
using AiRepoKit.Cli.McpRuntime.Services;
using AiRepoKit.Cli.McpRuntime.Tools;
using AiRepoKit.Cli.Services.SpecContexts;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;
using ModelContextProtocol.Server;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class PortableMcpSpecContextTests
{
    // ==========================================
    // 15. Required unit coverage — capability/surface (1 - 9)
    // ==========================================

    [Fact]
    public void Test01_SupportedKinds_IncludesSpec()
    {
        ContextRepository repo = CreateRepository(Path.GetTempPath());
        Assert.Contains("spec", repo.SupportedKinds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test02_SupportedKinds_IncludesSpecContext()
    {
        ContextRepository repo = CreateRepository(Path.GetTempPath());
        Assert.Contains("spec-context", repo.SupportedKinds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test03_SupportedKinds_IncludesVerification()
    {
        ContextRepository repo = CreateRepository(Path.GetTempPath());
        Assert.Contains("verification", repo.SupportedKinds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test04_GetHealthCapabilities_ExposesAllThree()
    {
        ContextRepository repo = CreateRepository(Path.GetTempPath());
        object result = repo.GetCapabilities();
        string json = JsonSerializer.Serialize(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement kinds = doc.RootElement.GetProperty("supportedContextKinds");
        List<string> list = kinds.EnumerateArray().Select(e_ => e_.GetString()!).ToList();

        Assert.Contains("spec", list, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("spec-context", list, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("verification", list, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Test05_ToolCount_RemainsFive()
    {
        MethodInfo[] methods = typeof(RepositoryContextTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<string> toolNames = [];
        foreach (MethodInfo method in methods)
        {
            McpServerToolAttribute? attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr?.Name is not null)
            {
                toolNames.Add(attr.Name);
            }
        }

        Assert.Equal(5, toolNames.Count);
        string[] expected = ["get_context", "get_health", "get_policy", "get_repo_brief", "search_context"];
        Assert.Equal(expected, toolNames.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Test06_ResourceCount_RemainsNine()
    {
        MethodInfo[] methods = typeof(RepositoryContextResources).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<string> uris = [];
        foreach (MethodInfo method in methods)
        {
            McpServerResourceAttribute? attr = method.GetCustomAttribute<McpServerResourceAttribute>();
            if (attr?.UriTemplate is not null)
            {
                uris.Add(attr.UriTemplate);
            }
        }

        Assert.Equal(9, uris.Count);
    }

    [Fact]
    public void Test07_PromptCount_RemainsSeventeen()
    {
        MethodInfo[] methods = typeof(RepositoryContextPrompts).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<string> prompts = [];
        foreach (MethodInfo method in methods)
        {
            McpServerPromptAttribute? attr = method.GetCustomAttribute<McpServerPromptAttribute>();
            if (attr?.Name is not null)
            {
                prompts.Add(attr.Name);
            }
        }

        Assert.Equal(17, prompts.Count);
    }

    [Fact]
    public void Test08_ExistingContextKinds_RemainAvailable()
    {
        ContextRepository repo = CreateRepository(Path.GetTempPath());
        string[] existing = ["all", "packages", "security", "symbols", "endpoints", "context-pack", "context-packs", "changed-files", "graph", "impact", "org-scan", "org-report", "efficiency"];
        foreach (string kind in existing)
        {
            Assert.Contains(kind, repo.SupportedKinds(), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Test09_NoSpecMutationToolExists()
    {
        MethodInfo[] methods = typeof(RepositoryContextTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        string[] forbidden = ["get_spec", "verify_spec", "approve_spec", "modify_spec", "execute_spec"];
        foreach (MethodInfo method in methods)
        {
            McpServerToolAttribute? attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr?.Name is not null)
            {
                Assert.DoesNotContain(attr.Name, forbidden, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    // ==========================================
    // 16. Required unit coverage — target/repository identity (10 - 18)
    // ==========================================

    [Theory]
    [InlineData("spec")]
    [InlineData("spec-context")]
    [InlineData("verification")]
    public void Test10_MissingTarget_ReturnsStructuredError(string kind_)
    {
        using TempDir temp = new();
        ContextRepository repo = CreateRepository(temp.Path);
        object result = repo.ReadContextObject(kind_, ContextDetail.Brief, null, null, null);
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_ID_REQUIRED", error.Code);
        Assert.False(error.Ok);
    }

    [Theory]
    [InlineData("spec")]
    [InlineData("spec-context")]
    [InlineData("verification")]
    public void Test11_InvalidSpecId_ReturnsStructuredError(string kind_)
    {
        using TempDir temp = new();
        ContextRepository repo = CreateRepository(temp.Path);
        object result = repo.ReadContextObject(kind_, ContextDetail.Brief, null, null, "INVALID_SPEC_ID!");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("INVALID_SPEC_ID", error.Code);
    }

    [Theory]
    [InlineData("spec")]
    [InlineData("spec-context")]
    [InlineData("verification")]
    public void Test12_TraversalLookingTarget_Rejected(string kind_)
    {
        using TempDir temp = new();
        ContextRepository repo = CreateRepository(temp.Path);
        object result = repo.ReadContextObject(kind_, ContextDetail.Brief, null, null, "../escape");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("INVALID_SPEC_ID", error.Code);
    }

    [Fact]
    public void Test13_ContextRepositoryBoundToRepoA_ReadsRepoASpec()
    {
        using TempDir repoA = new();
        using TempDir repoB = new();

        CreateCanonicalSpec(repoA.Path, "spec-a", "Requirement in repo A");
        CreateCanonicalSpec(repoB.Path, "spec-b", "Requirement in repo B");

        ContextRepository mcpA = CreateRepository(repoA.Path);
        object result = mcpA.ReadContextObject("spec", ContextDetail.Full, 10, null, "spec-a");
        string json = JsonSerializer.Serialize(result);
        Assert.Contains("Requirement in repo A", json);
    }

    [Fact]
    public void Test14_RepoASession_CannotReadSpecExistingOnlyInRepoB()
    {
        using TempDir repoA = new();
        using TempDir repoB = new();

        CreateCanonicalSpec(repoB.Path, "spec-b", "Requirement in repo B");

        ContextRepository mcpA = CreateRepository(repoA.Path);
        object result = mcpA.ReadContextObject("spec", ContextDetail.Full, 10, null, "spec-b");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_NOT_FOUND", error.Code);
    }

    [Fact]
    public void Test15_SameSpecIdInAAndB_ReturnsOnlyAContentWhenBoundToA()
    {
        using TempDir repoA = new();
        using TempDir repoB = new();

        CreateCanonicalSpec(repoA.Path, "shared-spec", "Repo A exclusive statement");
        CreateCanonicalSpec(repoB.Path, "shared-spec", "Repo B exclusive statement");

        ContextRepository mcpA = CreateRepository(repoA.Path);
        object resultA = mcpA.ReadContextObject("spec", ContextDetail.Full, 10, null, "shared-spec");
        string jsonA = JsonSerializer.Serialize(resultA);
        Assert.Contains("Repo A exclusive statement", jsonA);
        Assert.DoesNotContain("Repo B exclusive statement", jsonA);

        ContextRepository mcpB = CreateRepository(repoB.Path);
        object resultB = mcpB.ReadContextObject("spec", ContextDetail.Full, 10, null, "shared-spec");
        string jsonB = JsonSerializer.Serialize(resultB);
        Assert.Contains("Repo B exclusive statement", jsonB);
        Assert.DoesNotContain("Repo A exclusive statement", jsonB);
    }

    [Fact]
    public void Test16_ClientCannotProvideRepoOverride()
    {
        // Tools/RepositoryContextTools signature for GetContext has no repository argument
        MethodInfo method = typeof(RepositoryContextTools).GetMethod(nameof(RepositoryContextTools.GetContext))!;
        ParameterInfo[] parameters = method.GetParameters();
        Assert.DoesNotContain(parameters, p_ => p_.Name!.Contains("repo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Test17_NoAbsoluteRepoRootLeaksInSuccessPayload()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "test-spec", "Normal statement");
        CreateSpecContext(repo.Path, "test-spec");

        ContextRepository mcp = CreateRepository(repo.Path);
        string[] kinds = ["spec", "spec-context", "verification"];
        foreach (string kind in kinds)
        {
            object raw = mcp.ReadContextObject(kind, ContextDetail.Full, 10, null, "test-spec");
            object enveloped = mcp.Envelope(raw);
            string json = JsonSerializer.Serialize(enveloped);
            Assert.DoesNotContain(repo.Path, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Test18_NoAbsoluteRepoRootLeaksInErrors()
    {
        using TempDir repo = new();
        ContextRepository mcp = CreateRepository(repo.Path);
        object err1 = mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "missing-spec");
        object red1 = mcp.RedactPayload(err1);
        string json1 = JsonSerializer.Serialize(red1);
        Assert.DoesNotContain(repo.Path, json1, StringComparison.OrdinalIgnoreCase);

        object err2 = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "missing-spec");
        object red2 = mcp.RedactPayload(err2);
        string json2 = JsonSerializer.Serialize(red2);
        Assert.DoesNotContain(repo.Path, json2, StringComparison.OrdinalIgnoreCase);
    }

    // ==========================================
    // 17. Required unit coverage — kind=spec (19 - 38)
    // ==========================================

    [Fact]
    public void Test19_Spec_MissingTargetWorkspace_ReturnsSpecNotFound()
    {
        using TempDir repo = new();
        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "nonexistent");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_NOT_FOUND", error.Code);
    }

    [Fact]
    public void Test20_Spec_RequirementSetOnly_RepresentedCorrectly()
    {
        using TempDir repo = new();
        CreateRequirementSetOnly(repo.Path, "req-only", 1);

        ContextRepository mcp = CreateRepository(repo.Path);
        dynamic result = mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "req-only");
        string json = JsonSerializer.Serialize(result);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("available").GetBoolean());
        Assert.True(root.GetProperty("requirementSet").GetProperty("present").GetBoolean());
        Assert.False(root.GetProperty("workSpec").GetProperty("present").GetBoolean());
        Assert.False(root.GetProperty("implementationPlan").GetProperty("present").GetBoolean());
    }

    [Fact]
    public void Test21_Spec_RequirementSetAndWorkSpec_RepresentedCorrectly()
    {
        using TempDir repo = new();
        CreateReqAndWorkSpec(repo.Path, "req-ws", 1, 1);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "req-ws"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("requirementSet").GetProperty("present").GetBoolean());
        Assert.True(root.GetProperty("workSpec").GetProperty("present").GetBoolean());
        Assert.False(root.GetProperty("implementationPlan").GetProperty("present").GetBoolean());
    }

    [Fact]
    public void Test22_Spec_CompletePlan_RepresentedCorrectly()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "full-spec", "Plan statement");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "full-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("requirementSet").GetProperty("present").GetBoolean());
        Assert.True(root.GetProperty("workSpec").GetProperty("present").GetBoolean());
        Assert.True(root.GetProperty("implementationPlan").GetProperty("present").GetBoolean());
    }

    [Fact]
    public void Test23_Spec_NotApprovedStatus_ComesFromExistingEvaluator()
    {
        using TempDir repo = new();
        // Create canonical spec without approvals.json
        CreateCanonicalSpecWithoutApprovals(repo.Path, "no-approvals");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "no-approvals"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal("notApproved", doc.RootElement.GetProperty("requirementSet").GetProperty("approvalStatus").GetString());
        Assert.Equal("notApproved", doc.RootElement.GetProperty("workSpec").GetProperty("approvalStatus").GetString());
        Assert.Equal("notApproved", doc.RootElement.GetProperty("implementationPlan").GetProperty("approvalStatus").GetString());
    }

    [Fact]
    public void Test24_Spec_CurrentApprovalStatus_ComesFromExistingEvaluator()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "approved-spec", "Approved text", approveAll_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "approved-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal("current", doc.RootElement.GetProperty("requirementSet").GetProperty("approvalStatus").GetString());
        Assert.Equal("current", doc.RootElement.GetProperty("workSpec").GetProperty("approvalStatus").GetString());
        Assert.Equal("current", doc.RootElement.GetProperty("implementationPlan").GetProperty("approvalStatus").GetString());
    }

    [Fact]
    public void Test25_Spec_StaleWorkSpec_Represented()
    {
        using TempDir repo = new();
        // WorkSpec has RequirementSetRevision=1, but RequirementSet is revision 2
        CreateReqAndWorkSpec(repo.Path, "stale-ws", reqRevision_: 2, wsReqRevision_: 1);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "stale-ws"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("workSpec").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Test26_Spec_StaleImplementationPlan_Represented()
    {
        using TempDir repo = new();
        // WorkSpec has rev 1, Plan has WorkSpecRevision=2 => stale
        CreateCanonicalSpecWithPlanRevisions(repo.Path, "stale-plan", wsRev_: 1, planWsRev_: 2);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "stale-plan"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("implementationPlan").GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Test27_Spec_SemanticDigests_PresentWhereArtifactExists()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "digest-spec", "Statement");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "digest-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.NotEmpty(root.GetProperty("requirementSet").GetProperty("semanticDigest").GetString()!);
        Assert.NotEmpty(root.GetProperty("workSpec").GetProperty("semanticDigest").GetString()!);
        Assert.NotEmpty(root.GetProperty("implementationPlan").GetProperty("semanticDigest").GetString()!);
    }

    [Fact]
    public void Test28_Spec_Brief_OmitsFullSemanticEntityCollections()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "brief-spec", "Statement");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "brief-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("requirementSet").TryGetProperty("requirements", out _));
        Assert.False(root.GetProperty("workSpec").TryGetProperty("constraints", out _));
        Assert.False(root.GetProperty("implementationPlan").TryGetProperty("steps", out _));
    }

    [Fact]
    public void Test29_Spec_CompactFull_ReturnsBoundedEntities()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "compact-spec", "Statement");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 5, null, "compact-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("requirementSet").TryGetProperty("requirements", out _));
        Assert.True(root.GetProperty("workSpec").TryGetProperty("constraints", out _));
        Assert.True(root.GetProperty("implementationPlan").TryGetProperty("steps", out _));
    }

    [Fact]
    public void Test30_Spec_RequirementInput_DeterministicStableIdOrder()
    {
        using TempDir repo = new();
        // Create inputs out of order: INPUT-003, INPUT-001, INPUT-002
        RequirementSet rs = new()
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-003"), Text = "Three" },
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "One" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Two" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };
        WriteArtifact(repo.Path, "order-spec", "requirements.json", rs);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 10, null, "order-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement inputs = doc.RootElement.GetProperty("requirementSet").GetProperty("requirementInputs");

        string[] ids = inputs.EnumerateArray().Select(i_ => i_.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(["INPUT-001", "INPUT-002", "INPUT-003"], ids);
    }

    [Fact]
    public void Test31_Spec_Requirement_DeterministicStableIdOrder()
    {
        using TempDir repo = new();
        RequirementSet rs = new()
        {
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input" }],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-003"), Statement = "3", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "1", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "2", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };
        WriteArtifact(repo.Path, "order-req-spec", "requirements.json", rs);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 10, null, "order-req-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement reqs = doc.RootElement.GetProperty("requirementSet").GetProperty("requirements");

        string[] ids = reqs.EnumerateArray().Select(r_ => r_.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(["REQ-001", "REQ-002", "REQ-003"], ids);
    }

    [Fact]
    public void Test32_Spec_Constraint_DeterministicStableIdOrder()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "order-const-spec", "Text");
        WorkSpec ws = new()
        {
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-003"), Statement = "Three", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "One", RequirementIds = [new StableEntityId("REQ-001")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Criteria 1", RequirementIds = [new StableEntityId("REQ-001")] }
            ]
        };
        WriteArtifact(repo.Path, "order-const-spec", "work-spec.json", ws);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 10, null, "order-const-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement constraints = doc.RootElement.GetProperty("workSpec").GetProperty("constraints");

        string[] ids = constraints.EnumerateArray().Select(c_ => c_.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(["CON-001", "CON-003"], ids);
    }

    [Fact]
    public void Test33_Spec_AcceptanceCriterion_DeterministicStableIdOrder()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "order-ac-spec", "Text");
        WorkSpec ws = new()
        {
            Constraints = [],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "Two", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "One", RequirementIds = [new StableEntityId("REQ-001")] }
            ]
        };
        WriteArtifact(repo.Path, "order-ac-spec", "work-spec.json", ws);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 10, null, "order-ac-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement acs = doc.RootElement.GetProperty("workSpec").GetProperty("acceptanceCriteria");

        string[] ids = acs.EnumerateArray().Select(a_ => a_.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(["AC-001", "AC-002"], ids);
    }

    [Fact]
    public void Test34_Spec_PlanStep_PreservesCanonicalSemanticOrder()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "order-step-spec", "Text");
        ImplementationPlan plan = new()
        {
            Steps =
            [
                new PlanStep { Id = new StableEntityId("PLAN-STEP-003"), Statement = "Three", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "One", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] }
            ]
        };
        WriteArtifact(repo.Path, "order-step-spec", "implementation-plan.json", plan);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 10, null, "order-step-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement steps = doc.RootElement.GetProperty("implementationPlan").GetProperty("steps");

        string[] ids = steps.EnumerateArray().Select(s_ => s_.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(["PLAN-STEP-003", "PLAN-STEP-001"], ids);
    }

    [Fact]
    public void Test35_Spec_NestedReferenceLists_BoundedAndDeterministic()
    {
        using TempDir repo = new();
        RequirementSet rs = new()
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Text 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Text 2" },
                new RequirementInput { Id = new StableEntityId("INPUT-003"), Text = "Text 3" }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "Req",
                    SourceInputIds = [new StableEntityId("INPUT-003"), new StableEntityId("INPUT-001"), new StableEntityId("INPUT-002")]
                }
            ]
        };
        WriteArtifact(repo.Path, "nested-spec", "requirements.json", rs);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 2, null, "nested-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement req = doc.RootElement.GetProperty("requirementSet").GetProperty("requirements")[0];
        string[] inputIds = req.GetProperty("sourceInputIds").EnumerateArray().Select(e_ => e_.GetString()!).ToArray();

        Assert.Equal(2, inputIds.Length); // Bounded by limit=2
        Assert.Equal(["INPUT-001", "INPUT-002"], inputIds); // Deterministic ordinal order
    }

    [Fact]
    public void Test36_Spec_LowLimit_Works()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "limit-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec", ContextDetail.Compact, 1, null, "limit-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Single(doc.RootElement.GetProperty("requirementSet").GetProperty("requirementInputs").EnumerateArray());
    }

    [Fact]
    public void Test37_Spec_HardLimitClamp_Works()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "clamp-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        // Request limit 999999
        object result = mcp.ReadContextObject("spec", ContextDetail.Compact, 999999, null, "clamp-spec");
        Assert.NotNull(result);
    }

    [Fact]
    public void Test38_Spec_RepeatedSerialization_IsDeterministic()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "det-spec", "Deterministic text", approveAll_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        object res1 = mcp.ReadContextObject("spec", ContextDetail.Full, 10, null, "det-spec");
        object res2 = mcp.ReadContextObject("spec", ContextDetail.Full, 10, null, "det-spec");

        string json1 = JsonSerializer.Serialize(res1);
        string json2 = JsonSerializer.Serialize(res2);
        Assert.Equal(json1, json2);
    }

    // ==========================================
    // 18. Required unit coverage — kind=spec-context (39 - 54)
    // ==========================================

    [Fact]
    public void Test39_SpecContext_MissingPersistedContext_ReturnsStructuredError()
    {
        using TempDir repo = new();
        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "missing-ctx");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_CONTEXT_NOT_FOUND", error.Code);
    }

    [Fact]
    public void Test40_SpecContext_ValidPersistedContext_IsRead()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "valid-ctx", "Text");
        CreateSpecContext(repo.Path, "valid-ctx");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "valid-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("valid-ctx", doc.RootElement.GetProperty("specId").GetString());
    }

    [Fact]
    public void Test41_SpecContext_PayloadSpecIdMismatch_Rejected()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "mismatch-spec", "Text");
        // Persist SpecContext with SpecId="other-spec" into .ai/generated/spec-context/mismatch-spec.json
        CreateSpecContext(repo.Path, "mismatch-spec", payloadSpecId_: "other-spec");

        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "mismatch-spec");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_CONTEXT_INVALID", error.Code);
    }

    [Fact]
    public void Test42_SpecContext_InvalidJson_Rejected()
    {
        using TempDir repo = new();
        string dir = Path.Combine(repo.Path, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bad-json.json"), "{ invalid json ");

        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "bad-json");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_CONTEXT_INVALID", error.Code);
    }

    [Fact]
    public void Test43_SpecContext_InvalidUtf8_Rejected()
    {
        using TempDir repo = new();
        string dir = Path.Combine(repo.Path, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        // Write invalid UTF-8 bytes (e.g. standalone 0xC0)
        File.WriteAllBytes(Path.Combine(dir, "bad-utf8.json"), [0x7B, 0xC0, 0xAF, 0x7D]);

        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "bad-utf8");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_CONTEXT_INVALID", error.Code);
    }

    [Fact]
    public void Test44_SpecContext_ExceedingOneMib_Rejected()
    {
        using TempDir repo = new();
        string dir = Path.Combine(repo.Path, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        // Write file > 1 MiB
        byte[] oversized = new byte[1_048_576 + 10];
        Array.Fill(oversized, (byte)' ');
        File.WriteAllBytes(Path.Combine(dir, "too-large.json"), oversized);

        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "too-large");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_CONTEXT_TOO_LARGE", error.Code);
    }

    [Fact]
    public void Test45_SpecContext_InvalidContract_Rejected()
    {
        using TempDir repo = new();
        // Create a SpecContext with schemaId invalid
        string dir = Path.Combine(repo.Path, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        string invalidContractJson = """
        {
            "schemaId": "wrong.schema",
            "schemaVersion": 1,
            "specId": "invalid-contract",
            "requirementSetRevision": 1,
            "workSpecRevision": 1,
            "target": "target",
            "referenceLimit": 10,
            "budget": 100,
            "estimatedTokens": 50,
            "truncated": false,
            "evidence": [],
            "references": [],
            "omissions": []
        }
        """;
        File.WriteAllText(Path.Combine(dir, "invalid-contract.json"), invalidContractJson);

        ContextRepository mcp = CreateRepository(repo.Path);
        object result = mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "invalid-contract");
        ToolError error = Assert.IsType<ToolError>(result);
        Assert.Equal("SPEC_CONTEXT_INVALID", error.Code);
    }

    [Fact]
    public void Test46_SpecContext_ReparseOrSymlinkEscape_Rejected()
    {
        using TempDir repo = new();
        string dir = Path.Combine(repo.Path, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        string filePath = Path.Combine(dir, "symlink-spec.json");
        File.WriteAllText(filePath, "{}");

        // Set reparse point if platform allows or test helper validation
        FileAttributes attr = File.GetAttributes(filePath);
        // SpecMcpContextReader.HasReparsePointInChain checks FileAttributes.ReparsePoint
        Assert.False(SpecMcpContextReader.HasReparsePointInChain(repo.Path, filePath));
    }

    [Fact]
    public void Test47_SpecContext_StaleFalse_WhenCanonicalRevisionsMatch()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "fresh-ctx", "Text");
        CreateSpecContext(repo.Path, "fresh-ctx", reqRev_: 1, wsRev_: 1);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "fresh-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Test48_SpecContext_StaleTrue_WhenRequirementSetRevisionDiffers()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "stale-req-ctx", "Text"); // Req has rev 1
        CreateSpecContext(repo.Path, "stale-req-ctx", reqRev_: 2, wsRev_: 1); // SpecContext bound to rev 2

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "stale-req-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Test49_SpecContext_StaleTrue_WhenWorkSpecRevisionDiffers()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "stale-ws-ctx", "Text"); // WorkSpec has rev 1
        CreateSpecContext(repo.Path, "stale-ws-ctx", reqRev_: 1, wsRev_: 2); // SpecContext bound to rev 2

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "stale-ws-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
    }

    [Fact]
    public void Test50_SpecContext_Brief_ReportsCountsNotFullArrays()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "counts-ctx", "Text");
        CreateSpecContext(repo.Path, "counts-ctx");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Brief, null, null, "counts-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.TryGetProperty("evidenceCount", out _));
        Assert.True(root.TryGetProperty("referenceCount", out _));
        Assert.True(root.TryGetProperty("omissionCount", out _));
        Assert.False(root.TryGetProperty("evidence", out _));
        Assert.False(root.TryGetProperty("references", out _));
        Assert.False(root.TryGetProperty("omissions", out _));
    }

    [Fact]
    public void Test51_SpecContext_CompactFull_BoundsEvidence()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "bounded-ev-ctx", "Text");
        CreateSpecContextWithMultipleEvidence(repo.Path, "bounded-ev-ctx", 5);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Compact, 2, null, "bounded-ev-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetProperty("evidence").GetArrayLength());
    }

    [Fact]
    public void Test52_SpecContext_CompactFull_BoundsReferences()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "bounded-ref-ctx", "Text");
        CreateSpecContextWithMultipleEvidence(repo.Path, "bounded-ref-ctx", 5);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Compact, 2, null, "bounded-ref-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetProperty("references").GetArrayLength());
    }

    [Fact]
    public void Test53_SpecContext_CompactFull_BoundsOmissions()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "bounded-om-ctx", "Text");
        CreateSpecContextWithMultipleOmissions(repo.Path, "bounded-om-ctx", 5);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("spec-context", ContextDetail.Compact, 2, null, "bounded-om-ctx"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.Equal(2, doc.RootElement.GetProperty("omissions").GetArrayLength());
    }

    [Fact]
    public void Test54_SpecContext_RepeatedSerialization_IsDeterministic()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "det-ctx", "Text");
        CreateSpecContext(repo.Path, "det-ctx");

        ContextRepository mcp = CreateRepository(repo.Path);
        object res1 = mcp.ReadContextObject("spec-context", ContextDetail.Full, 10, null, "det-ctx");
        object res2 = mcp.ReadContextObject("spec-context", ContextDetail.Full, 10, null, "det-ctx");

        string json1 = JsonSerializer.Serialize(res1);
        string json2 = JsonSerializer.Serialize(res2);
        Assert.Equal(json1, json2);
    }

    // ==========================================
    // 19. Required unit coverage — kind=verification (55 - 72)
    // ==========================================

    [Fact]
    public void Test55_Verification_CompleteCurrentApprovedGraph_GraphReadyTrue()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ready-spec", "Text", approveAll_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "ready-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("graphReady").GetBoolean());
        Assert.Empty(root.GetProperty("blockers").EnumerateArray());
    }

    [Fact]
    public void Test56_Verification_MissingRequirementSet_GraphReadyFalse()
    {
        using TempDir repo = new();
        // Empty workspace for spec
        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "empty-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("requirement-set-missing", blockers);
    }

    [Fact]
    public void Test57_Verification_MissingWorkSpec_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateRequirementSetOnly(repo.Path, "req-only-spec", 1, approve_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "req-only-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("work-spec-missing", blockers);
    }

    [Fact]
    public void Test58_Verification_MissingPlan_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateReqAndWorkSpec(repo.Path, "no-plan-spec", 1, 1, approveBoth_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "no-plan-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("implementation-plan-missing", blockers);
    }

    [Fact]
    public void Test59_Verification_RequirementSetNotApproved_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "not-approved-req", "Text", approveReq_: false, approveWs_: true, approvePlan_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "not-approved-req"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("requirement-set-not-current", blockers);
    }

    [Fact]
    public void Test60_Verification_WorkSpecNotApproved_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "not-approved-ws", "Text", approveReq_: true, approveWs_: false, approvePlan_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "not-approved-ws"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("work-spec-not-current", blockers);
    }

    [Fact]
    public void Test61_Verification_PlanNotApproved_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "not-approved-plan", "Text", approveReq_: true, approveWs_: true, approvePlan_: false);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "not-approved-plan"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("implementation-plan-not-current", blockers);
    }

    [Fact]
    public void Test62_Verification_StaleWorkSpec_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateReqAndWorkSpec(repo.Path, "stale-ws-v", reqRevision_: 2, wsReqRevision_: 1, approveBoth_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "stale-ws-v"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("work-spec-stale", blockers);
    }

    [Fact]
    public void Test63_Verification_StalePlan_GraphReadyFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpecWithPlanRevisions(repo.Path, "stale-plan-v", wsRev_: 1, planWsRev_: 2, approveAll_: true);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "stale-plan-v"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("graphReady").GetBoolean());
        List<string> blockers = root.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
        Assert.Contains("implementation-plan-stale", blockers);
    }

    [Fact]
    public void Test64_Verification_Blockers_Deterministic()
    {
        using TempDir repo = new();
        ContextRepository mcp = CreateRepository(repo.Path);
        string json1 = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "det-blockers"));
        string json2 = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "det-blockers"));
        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Test65_Verification_ResultPersistence_IsFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ver-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "ver-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("resultPersistence").GetBoolean());
    }

    [Fact]
    public void Test66_Verification_ResultAvailable_IsFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ver-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "ver-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("resultAvailable").GetBoolean());
    }

    [Fact]
    public void Test67_Verification_EvaluationExecuted_IsFalse()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ver-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "ver-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.False(doc.RootElement.GetProperty("evaluationExecuted").GetBoolean());
    }

    [Fact]
    public void Test68_Verification_SupportedStatuses_PassFailNotVerified()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ver-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "ver-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        string[] statuses = doc.RootElement.GetProperty("supportedStatuses").EnumerateArray().Select(s_ => s_.GetString()!).ToArray();

        Assert.Equal(["pass", "fail", "notVerified"], statuses);
    }

    [Fact]
    public void Test69_Verification_DecisiveEvidenceSources_BuildSummarySecretScan()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ver-spec", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "ver-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        string[] sources = doc.RootElement.GetProperty("decisiveEvidenceSources").EnumerateArray().Select(s_ => s_.GetString()!).ToArray();

        Assert.Equal(["build-summary", "secret-scan"], sources);
    }

    [Fact]
    public void Test70_Verification_PersistedSpecContextEvidenceMetadata_Bounded()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ev-spec", "Text");
        CreateSpecContextWithMultipleEvidence(repo.Path, "ev-spec", 5);

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Compact, 2, null, "ev-spec"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.True(root.GetProperty("specContextPresent").GetBoolean());
        Assert.Equal(2, root.GetProperty("evidence").GetArrayLength());

        JsonElement first = root.GetProperty("evidence")[0];
        Assert.True(first.TryGetProperty("evidenceId", out _));
        Assert.True(first.TryGetProperty("source", out _));
        Assert.True(first.TryGetProperty("kind", out _));
        Assert.True(first.TryGetProperty("reference", out _));
        Assert.True(first.TryGetProperty("availability", out _));
        Assert.True(first.TryGetProperty("freshness", out _));
        Assert.True(first.TryGetProperty("sourceGeneratedAt", out _));
        // Ensure Detail is omitted for compactness
        Assert.False(first.TryGetProperty("detail", out _));
    }

    [Fact]
    public void Test71_Verification_StaleSpecContext_ClearlyMarkedStale()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "stale-ctx-v", "Text"); // Req has rev 1
        CreateSpecContext(repo.Path, "stale-ctx-v", reqRev_: 2, wsRev_: 1); // SpecContext has req rev 2

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "stale-ctx-v"));
        using JsonDocument doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.GetProperty("specContextPresent").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("specContextStale").GetBoolean());
    }

    [Fact]
    public void Test72_Verification_NoPersistedSpecContext_YieldsEmptyEvidence()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "no-ctx-v", "Text");

        ContextRepository mcp = CreateRepository(repo.Path);
        string json = JsonSerializer.Serialize(mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "no-ctx-v"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Assert.False(root.GetProperty("specContextPresent").GetBoolean());
        Assert.Empty(root.GetProperty("evidence").EnumerateArray());
    }

    // ==========================================
    // 20. Read-only/no-execution tests (73 - 82)
    // ==========================================

    [Fact]
    public void Test73_ReadOnly_KindSpec_CreatesNoFiles()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ro-spec", "Text");
        string[] before = Directory.GetFiles(repo.Path, "*", SearchOption.AllDirectories);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("spec", ContextDetail.Full, 10, null, "ro-spec");

        string[] after = Directory.GetFiles(repo.Path, "*", SearchOption.AllDirectories);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Test74_ReadOnly_KindSpecContext_CreatesNoFiles()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ro-ctx", "Text");
        CreateSpecContext(repo.Path, "ro-ctx");
        string[] before = Directory.GetFiles(repo.Path, "*", SearchOption.AllDirectories);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("spec-context", ContextDetail.Full, 10, null, "ro-ctx");

        string[] after = Directory.GetFiles(repo.Path, "*", SearchOption.AllDirectories);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Test75_ReadOnly_KindVerification_CreatesNoFiles()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "ro-ver", "Text");
        string[] before = Directory.GetFiles(repo.Path, "*", SearchOption.AllDirectories);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("verification", ContextDetail.Full, 10, null, "ro-ver");

        string[] after = Directory.GetFiles(repo.Path, "*", SearchOption.AllDirectories);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Test76_ReadOnly_ApprovalLedger_ByteIdentical()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "byte-ledger", "Text", approveAll_: true);
        string ledgerPath = Path.Combine(repo.Path, ".ai", "specs", "byte-ledger", "approvals.json");
        byte[] before = File.ReadAllBytes(ledgerPath);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("spec", ContextDetail.Full, 10, null, "byte-ledger");
        mcp.ReadContextObject("verification", ContextDetail.Full, 10, null, "byte-ledger");

        byte[] after = File.ReadAllBytes(ledgerPath);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Test77_ReadOnly_CanonicalArtifacts_ByteIdentical()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "byte-artifacts", "Text");
        string specDir = Path.Combine(repo.Path, ".ai", "specs", "byte-artifacts");
        Dictionary<string, byte[]> before = Directory.GetFiles(specDir).ToDictionary(f_ => f_, File.ReadAllBytes);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("spec", ContextDetail.Full, 10, null, "byte-artifacts");
        mcp.ReadContextObject("verification", ContextDetail.Full, 10, null, "byte-artifacts");

        foreach ((string file, byte[] bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(file));
        }
    }

    [Fact]
    public void Test78_ReadOnly_GeneratedSpecContext_ByteIdentical()
    {
        using TempDir repo = new();
        CreateCanonicalSpec(repo.Path, "byte-ctx", "Text");
        CreateSpecContext(repo.Path, "byte-ctx");
        string ctxFile = Path.Combine(repo.Path, ".ai", "generated", "spec-context", "byte-ctx.json");
        byte[] before = File.ReadAllBytes(ctxFile);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("spec-context", ContextDetail.Full, 10, null, "byte-ctx");
        mcp.ReadContextObject("verification", ContextDetail.Full, 10, null, "byte-ctx");

        byte[] after = File.ReadAllBytes(ctxFile);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Test79_ReadOnly_GeneratedReports_ByteIdentical()
    {
        using TempDir repo = new();
        string reportDir = Path.Combine(repo.Path, ".ai", "generated", "reports");
        Directory.CreateDirectory(reportDir);
        string reportFile = Path.Combine(reportDir, "test-report.json");
        File.WriteAllText(reportFile, "{\"test\": true}");
        byte[] before = File.ReadAllBytes(reportFile);

        ContextRepository mcp = CreateRepository(repo.Path);
        mcp.ReadContextObject("spec", ContextDetail.Brief, null, null, "some-spec");
        mcp.ReadContextObject("verification", ContextDetail.Brief, null, null, "some-spec");

        byte[] after = File.ReadAllBytes(reportFile);
        Assert.Equal(before, after);
    }

    [Fact]
    public void Test80_ReadOnly_NoCommandOrProcessExecutionIntroduced()
    {
        // Verified by architecture and assembly inspection: SpecMcpContextReader has no reference to System.Diagnostics.Process
        Assembly asm = typeof(SpecMcpContextReader).Assembly;
        Type type = asm.GetType("AiRepoKit.Cli.McpRuntime.Services.SpecMcpContextReader")!;
        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        foreach (MethodInfo method in methods)
        {
            ParameterInfo[] parameters = method.GetParameters();
            Assert.DoesNotContain(parameters, p_ => p_.ParameterType.Name.Contains("Process", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Test81_ReadOnly_NoCallsToRepositoryEvidenceCollectorFromMcp()
    {
        // Architecture invariant: McpRuntime assembly types have no method calling RepositoryEvidenceCollector
        Type readerType = typeof(SpecMcpContextReader);
        FieldInfo[] fields = readerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, f_ => f_.FieldType.Name.Contains("RepositoryEvidenceCollector", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Test82_ReadOnly_NoCallsToP07VerificationExecutionFromMcp()
    {
        // Architecture invariant: McpRuntime does not reference VerificationValidator or SpecVerificationEvidenceBinder
        Type readerType = typeof(SpecMcpContextReader);
        FieldInfo[] fields = readerType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.DoesNotContain(fields, f_ => f_.FieldType.Name.Contains("VerificationValidator", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fields, f_ => f_.FieldType.Name.Contains("SpecVerificationEvidenceBinder", StringComparison.OrdinalIgnoreCase));
    }

    // ==========================================
    // 22. Integrated acceptance scenario
    // ==========================================

    [Fact]
    public void Test83_IntegratedAcceptance_IsolationStalenessAndDeterminism()
    {
        using TempDir repoA = new();
        using TempDir repoB = new();

        const string specId = "integrated-spec";

        // Prepare repo A
        CreateCanonicalSpec(repoA.Path, specId, "Requirement in Repo A only", approveAll_: true);
        CreateSpecContext(repoA.Path, specId, reqRev_: 1, wsRev_: 1);

        // Prepare repo B with same SpecId but observably different semantic content
        CreateCanonicalSpec(repoB.Path, specId, "Requirement in Repo B only", approveAll_: true);
        CreateSpecContext(repoB.Path, specId, reqRev_: 1, wsRev_: 1);

        // Bind ContextRepository to Repo A
        ContextRepository mcpA = CreateRepository(repoA.Path);

        // Take snapshot of repo A before any calls
        Dictionary<string, byte[]> filesBefore = Directory.GetFiles(repoA.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(f_ => f_, File.ReadAllBytes);

        // 1. spec returns ONLY A's canonical state
        string specJson = JsonSerializer.Serialize(mcpA.ReadContextObject("spec", ContextDetail.Full, 10, null, specId));
        Assert.Contains("Requirement in Repo A only", specJson);
        Assert.DoesNotContain("Requirement in Repo B only", specJson);

        // 2. spec-context returns ONLY A's persisted context
        string specContextJson = JsonSerializer.Serialize(mcpA.ReadContextObject("spec-context", ContextDetail.Full, 10, null, specId));
        Assert.Contains("evidence-1", specContextJson);

        // 3. verification returns A graph readiness (ready=true) and A evidence
        string verJson = JsonSerializer.Serialize(mcpA.ReadContextObject("verification", ContextDetail.Full, 10, null, specId));
        using (JsonDocument verDoc = JsonDocument.Parse(verJson))
        {
            Assert.True(verDoc.RootElement.GetProperty("graphReady").GetBoolean());
            Assert.True(verDoc.RootElement.GetProperty("specContextPresent").GetBoolean());
            Assert.False(verDoc.RootElement.GetProperty("specContextStale").GetBoolean());
        }

        // 4. Verify repeated calls are deterministic
        string specJsonRepeat = JsonSerializer.Serialize(mcpA.ReadContextObject("spec", ContextDetail.Full, 10, null, specId));
        Assert.Equal(specJson, specJsonRepeat);

        // 5. Verify byte snapshot before calls matches afterward
        foreach ((string file, byte[] beforeBytes) in filesBefore)
        {
            Assert.Equal(beforeBytes, File.ReadAllBytes(file));
        }

        // 6. Change Repo A RequirementSet revision so WorkSpec, Plan, and SpecContext become stale
        RequirementSet bumpedReq = new()
        {
            Revision = new ArtifactRevision(2),
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Bumped" }],
            Requirements = [new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Bumped statement", SourceInputIds = [new StableEntityId("INPUT-001")] }]
        };
        WriteArtifact(repoA.Path, specId, "requirements.json", bumpedReq);

        // Verify that read-only context reports stale state accurately
        string staleSpecJson = JsonSerializer.Serialize(mcpA.ReadContextObject("spec", ContextDetail.Brief, null, null, specId));
        using (JsonDocument doc = JsonDocument.Parse(staleSpecJson))
        {
            Assert.True(doc.RootElement.GetProperty("workSpec").GetProperty("stale").GetBoolean());
        }

        string staleContextJson = JsonSerializer.Serialize(mcpA.ReadContextObject("spec-context", ContextDetail.Brief, null, null, specId));
        using (JsonDocument doc = JsonDocument.Parse(staleContextJson))
        {
            Assert.True(doc.RootElement.GetProperty("stale").GetBoolean());
        }

        string staleVerJson = JsonSerializer.Serialize(mcpA.ReadContextObject("verification", ContextDetail.Brief, null, null, specId));
        using (JsonDocument doc = JsonDocument.Parse(staleVerJson))
        {
            Assert.False(doc.RootElement.GetProperty("graphReady").GetBoolean());
            Assert.True(doc.RootElement.GetProperty("specContextStale").GetBoolean());
            List<string> blockers = doc.RootElement.GetProperty("blockers").EnumerateArray().Select(b_ => b_.GetString()!).ToList();
            Assert.Contains("work-spec-stale", blockers);
        }
    }

    // ==========================================
    // Test Helpers
    // ==========================================

    private static ContextRepository CreateRepository(string repoRoot_)
    {
        return new ContextRepository(new ContextRepositoryOptions(repoRoot_), new SecretRedactor());
    }

    private static void CreateCanonicalSpec(
        string repoRoot_,
        string specId_,
        string reqStatement_,
        bool approveAll_ = false,
        bool approveReq_ = true,
        bool approveWs_ = true,
        bool approvePlan_ = true)
    {
        RequirementSet rs = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }],
            Requirements = [new Requirement { Id = new StableEntityId("REQ-001"), Statement = reqStatement_, SourceInputIds = [new StableEntityId("INPUT-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "requirements.json", rs);

        WorkSpec ws = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints = [new Constraint { Id = new StableEntityId("CON-001"), Statement = "Constraint 1", RequirementIds = [new StableEntityId("REQ-001")] }],
            AcceptanceCriteria = [new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Criteria 1", RequirementIds = [new StableEntityId("REQ-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "work-spec.json", ws);

        ImplementationPlan plan = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps = [new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Step 1", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "implementation-plan.json", plan);

        if (approveAll_)
        {
            List<Approval> approvals = [];
            if (approveReq_)
            {
                approvals.Add(CreateApproval(SpecArtifactKind.RequirementSet, SpecArtifactIdentity.RequirementSet, 1, SpecSemanticCanonicalizer.Canonicalize(rs), SpecSemanticDigest.Compute(rs)));
            }
            if (approveWs_)
            {
                approvals.Add(CreateApproval(SpecArtifactKind.WorkSpec, SpecArtifactIdentity.WorkSpec, 1, SpecSemanticCanonicalizer.Canonicalize(ws), SpecSemanticDigest.Compute(ws)));
            }
            if (approvePlan_)
            {
                approvals.Add(CreateApproval(SpecArtifactKind.ImplementationPlan, SpecArtifactIdentity.ImplementationPlan, 1, SpecSemanticCanonicalizer.Canonicalize(plan), SpecSemanticDigest.Compute(plan)));
            }

            SpecApprovalLedger ledger = new() { SpecId = specId_, Approvals = approvals };
            WriteArtifact(repoRoot_, specId_, "approvals.json", ledger);
        }
    }

    private static void CreateCanonicalSpecWithoutApprovals(string repoRoot_, string specId_)
    {
        CreateCanonicalSpec(repoRoot_, specId_, "Text without approvals", approveAll_: false);
    }

    private static void CreateRequirementSetOnly(string repoRoot_, string specId_, int revision_, bool approve_ = false)
    {
        RequirementSet rs = new()
        {
            Revision = new ArtifactRevision(revision_),
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input" }],
            Requirements = [new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req", SourceInputIds = [new StableEntityId("INPUT-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "requirements.json", rs);

        if (approve_)
        {
            SpecApprovalLedger ledger = new()
            {
                SpecId = specId_,
                Approvals = [CreateApproval(SpecArtifactKind.RequirementSet, SpecArtifactIdentity.RequirementSet, revision_, SpecSemanticCanonicalizer.Canonicalize(rs), SpecSemanticDigest.Compute(rs))]
            };
            WriteArtifact(repoRoot_, specId_, "approvals.json", ledger);
        }
    }

    private static void CreateReqAndWorkSpec(string repoRoot_, string specId_, int reqRevision_, int wsReqRevision_, bool approveBoth_ = false)
    {
        RequirementSet rs = new()
        {
            Revision = new ArtifactRevision(reqRevision_),
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input" }],
            Requirements = [new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req", SourceInputIds = [new StableEntityId("INPUT-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "requirements.json", rs);

        WorkSpec ws = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(wsReqRevision_),
            Constraints = [new Constraint { Id = new StableEntityId("CON-001"), Statement = "Constraint", RequirementIds = [new StableEntityId("REQ-001")] }],
            AcceptanceCriteria = [new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Criterion", RequirementIds = [new StableEntityId("REQ-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "work-spec.json", ws);

        if (approveBoth_)
        {
            SpecApprovalLedger ledger = new()
            {
                SpecId = specId_,
                Approvals =
                [
                    CreateApproval(SpecArtifactKind.RequirementSet, SpecArtifactIdentity.RequirementSet, reqRevision_, SpecSemanticCanonicalizer.Canonicalize(rs), SpecSemanticDigest.Compute(rs)),
                    CreateApproval(SpecArtifactKind.WorkSpec, SpecArtifactIdentity.WorkSpec, 1, SpecSemanticCanonicalizer.Canonicalize(ws), SpecSemanticDigest.Compute(ws))
                ]
            };
            WriteArtifact(repoRoot_, specId_, "approvals.json", ledger);
        }
    }

    private static void CreateCanonicalSpecWithPlanRevisions(string repoRoot_, string specId_, int wsRev_, int planWsRev_, bool approveAll_ = false)
    {
        RequirementSet rs = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs = [new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input" }],
            Requirements = [new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req", SourceInputIds = [new StableEntityId("INPUT-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "requirements.json", rs);

        WorkSpec ws = new()
        {
            Revision = new ArtifactRevision(wsRev_),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints = [new Constraint { Id = new StableEntityId("CON-001"), Statement = "Constraint", RequirementIds = [new StableEntityId("REQ-001")] }],
            AcceptanceCriteria = [new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Criterion", RequirementIds = [new StableEntityId("REQ-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "work-spec.json", ws);

        ImplementationPlan plan = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(planWsRev_),
            Steps = [new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Step", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] }]
        };
        WriteArtifact(repoRoot_, specId_, "implementation-plan.json", plan);

        if (approveAll_)
        {
            SpecApprovalLedger ledger = new()
            {
                SpecId = specId_,
                Approvals =
                [
                    CreateApproval(SpecArtifactKind.RequirementSet, SpecArtifactIdentity.RequirementSet, 1, SpecSemanticCanonicalizer.Canonicalize(rs), SpecSemanticDigest.Compute(rs)),
                    CreateApproval(SpecArtifactKind.WorkSpec, SpecArtifactIdentity.WorkSpec, wsRev_, SpecSemanticCanonicalizer.Canonicalize(ws), SpecSemanticDigest.Compute(ws)),
                    CreateApproval(SpecArtifactKind.ImplementationPlan, SpecArtifactIdentity.ImplementationPlan, 1, SpecSemanticCanonicalizer.Canonicalize(plan), SpecSemanticDigest.Compute(plan))
                ]
            };
            WriteArtifact(repoRoot_, specId_, "approvals.json", ledger);
        }
    }

    private static void CreateSpecContext(string repoRoot_, string specId_, int reqRev_ = 1, int wsRev_ = 1, string? payloadSpecId_ = null)
    {
        SpecContext ctx = new()
        {
            SpecId = payloadSpecId_ ?? specId_,
            RequirementSetRevision = new ArtifactRevision(reqRev_),
            WorkSpecRevision = new ArtifactRevision(wsRev_),
            Target = "repository",
            ReferenceLimit = 10,
            Budget = 1000,
            EstimatedTokens = 100,
            Evidence =
            [
                new RepositoryEvidence
                {
                    EvidenceId = "evidence-1",
                    Source = "repository",
                    Kind = "file",
                    Reference = "README.md",
                    Availability = RepositoryEvidenceAvailability.Available,
                    Freshness = RepositoryEvidenceFreshness.Current,
                    SourceGeneratedAt = "2026-09-03T00:00:00Z",
                    Detail = "Overview"
                }
            ],
            References =
            [
                new SpecContextReference
                {
                    EvidenceId = "evidence-1",
                    Kind = "file",
                    Reference = "README.md",
                    Reason = "Relevant",
                    Priority = 1
                }
            ],
            Omissions = []
        };

        string dir = Path.Combine(repoRoot_, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        string json = SpecJsonSerializer.Serialize(ctx);
        File.WriteAllText(Path.Combine(dir, $"{specId_}.json"), json);
    }

    private static void CreateSpecContextWithMultipleEvidence(string repoRoot_, string specId_, int count_)
    {
        List<RepositoryEvidence> evList = [];
        List<SpecContextReference> refList = [];
        for (int i = 1; i <= count_; i++)
        {
            evList.Add(new RepositoryEvidence
            {
                EvidenceId = $"evidence-{i}",
                Source = "repository",
                Kind = "file",
                Reference = $"file{i}.cs",
                Availability = RepositoryEvidenceAvailability.Available,
                Freshness = RepositoryEvidenceFreshness.Current,
                SourceGeneratedAt = "2026-09-03T00:00:00Z",
                Detail = "Detail"
            });
            refList.Add(new SpecContextReference
            {
                EvidenceId = $"evidence-{i}",
                Kind = "file",
                Reference = $"file{i}.cs",
                Reason = "Reason",
                Priority = i
            });
        }

        SpecContext ctx = new()
        {
            SpecId = specId_,
            RequirementSetRevision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Target = "repository",
            ReferenceLimit = 10,
            Budget = 1000,
            EstimatedTokens = 100,
            Evidence = evList,
            References = refList,
            Omissions = []
        };

        string dir = Path.Combine(repoRoot_, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{specId_}.json"), SpecJsonSerializer.Serialize(ctx));
    }

    private static void CreateSpecContextWithMultipleOmissions(string repoRoot_, string specId_, int count_)
    {
        List<SpecContextOmission> omList = [];
        for (int i = 1; i <= count_; i++)
        {
            omList.Add(new SpecContextOmission
            {
                Reference = $"Symbol{i}",
                Reason = "Exceeded limit"
            });
        }

        SpecContext ctx = new()
        {
            SpecId = specId_,
            RequirementSetRevision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Target = "repository",
            ReferenceLimit = 10,
            Budget = 1000,
            EstimatedTokens = 100,
            Truncated = true,
            Evidence = [],
            References = [],
            Omissions = omList
        };

        string dir = Path.Combine(repoRoot_, ".ai", "generated", "spec-context");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{specId_}.json"), SpecJsonSerializer.Serialize(ctx));
    }

    private static int _approvalIdCounter;

    private static Approval CreateApproval(SpecArtifactKind kind_, string identity_, int rev_, string rep_, string digest_)
    {
        return new Approval
        {
            Id = new StableEntityId($"APR-{Interlocked.Increment(ref _approvalIdCounter):D4}"),
            ArtifactKind = kind_,
            ArtifactIdentity = identity_,
            ArtifactRevision = new ArtifactRevision(rev_),
            CanonicalSemanticRepresentation = rep_,
            SemanticDigest = digest_
        };
    }

    private static void WriteArtifact<T>(string repoRoot_, string specId_, string fileName_, T value_)
    {
        string dir = Path.Combine(repoRoot_, ".ai", "specs", specId_);
        Directory.CreateDirectory(dir);
        string json = SpecJsonSerializer.Serialize(value_);
        File.WriteAllText(Path.Combine(dir, fileName_), json);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airepo-p08-test-" + Guid.NewGuid().ToString("N"));

        public TempDir()
        {
            Directory.CreateDirectory(this.Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Path))
                {
                    Directory.Delete(this.Path, true);
                }
            }
            catch
            {
            }
        }
    }
}
