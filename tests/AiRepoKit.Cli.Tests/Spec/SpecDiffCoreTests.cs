using AiRepoKit.Spec;
using AiRepoKit.Spec.Diff;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecDiffCoreTests
{
    private static readonly SpecId _specId = new("test-spec");

    // 1. RequirementInput Added
    [Fact]
    public void RequirementInput_Added_Detected()
    {
        RequirementSet current = CreateRequirementSet();
        RequirementSet candidate = current with
        {
            Inputs =
            [
                .. current.Inputs,
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "New input" }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        SpecEntityChange added = Assert.Single(result.EntityChanges, c_ => c_.EntityId == "INPUT-002");
        Assert.Equal(SpecChangeKind.Added, added.ChangeKind);
        Assert.Equal(SpecDiffEntityKind.RequirementInput, added.EntityKind);
    }

    // 2. RequirementInput Modified
    [Fact]
    public void RequirementInput_Modified_Detected()
    {
        RequirementSet current = CreateRequirementSet();
        RequirementSet candidate = current with
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Modified text" }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        SpecEntityChange change = Assert.Single(result.EntityChanges, c_ => c_.EntityId == "INPUT-001");
        Assert.Equal(SpecChangeKind.Modified, change.ChangeKind);
    }

    // 3. RequirementInput Removed
    [Fact]
    public void RequirementInput_Removed_Detected()
    {
        RequirementSet current = CreateRequirementSet() with
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "Req 1",
                    SourceInputIds = [new StableEntityId("INPUT-001")]
                }
            ]
        };

        RequirementSet candidate = current with
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        SpecEntityChange change = Assert.Single(result.EntityChanges, c_ => c_.EntityId == "INPUT-002");
        Assert.Equal(SpecChangeKind.Removed, change.ChangeKind);
    }

    // 4. RequirementInput Unchanged
    [Fact]
    public void RequirementInput_Unchanged_Detected()
    {
        RequirementSet current = CreateRequirementSet();
        RequirementSet candidate = CreateRequirementSet();

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        SpecEntityChange change = Assert.Single(result.EntityChanges, c_ => c_.EntityId == "INPUT-001");
        Assert.Equal(SpecChangeKind.Unchanged, change.ChangeKind);
    }

    // 5. Requirement Added/Modified/Removed/Unchanged
    [Fact]
    public void Requirement_Added_Modified_Removed_Unchanged_Detected()
    {
        RequirementSet current = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Keep unchanged", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "To modify", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-003"), Statement = "To remove", SourceInputIds = [new StableEntityId("INPUT-002")] }
            ]
        };

        RequirementSet candidate = current with
        {
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Keep unchanged", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Modified statement", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-004"), Statement = "Newly added", SourceInputIds = [new StableEntityId("INPUT-002")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        Assert.Equal(SpecChangeKind.Unchanged, result.EntityChanges.Single(c_ => c_.EntityId == "REQ-001").ChangeKind);
        Assert.Equal(SpecChangeKind.Modified, result.EntityChanges.Single(c_ => c_.EntityId == "REQ-002").ChangeKind);
        Assert.Equal(SpecChangeKind.Removed, result.EntityChanges.Single(c_ => c_.EntityId == "REQ-003").ChangeKind);
        Assert.Equal(SpecChangeKind.Added, result.EntityChanges.Single(c_ => c_.EntityId == "REQ-004").ChangeKind);
    }

    // 6. Requirement reference-ID reordering does not modify the Requirement
    [Fact]
    public void Requirement_ReferenceId_Reordering_DoesNotModifyRequirement()
    {
        RequirementSet current = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "Multi input req",
                    SourceInputIds = [new StableEntityId("INPUT-001"), new StableEntityId("INPUT-002")]
                }
            ]
        };

        RequirementSet candidate = current with
        {
            Requirements =
            [
                new Requirement
                {
                    Id = new StableEntityId("REQ-001"),
                    Statement = "Multi input req",
                    SourceInputIds = [new StableEntityId("INPUT-002"), new StableEntityId("INPUT-001")]
                }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.Equal(SpecChangeKind.Unchanged, result.EntityChanges.Single(c_ => c_.EntityId == "REQ-001").ChangeKind);
    }

    // 7. RequirementSet collection reorder is semantic no-op
    [Fact]
    public void RequirementSet_CollectionReorder_IsSemanticNoOp()
    {
        RequirementSet current = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req 1", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Req 2", SourceInputIds = [new StableEntityId("INPUT-002")] }
            ]
        };

        RequirementSet candidate = current with
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" },
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Req 2", SourceInputIds = [new StableEntityId("INPUT-002")] },
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req 1", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.All(result.EntityChanges, c_ => Assert.Equal(SpecChangeKind.Unchanged, c_.ChangeKind));
    }

    // 8. Constraint Added/Modified/Removed/Unchanged
    [Fact]
    public void Constraint_Added_Modified_Removed_Unchanged_Detected()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec current = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Keep unchanged", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "To modify", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-003"), Statement = "To remove", RequirementIds = [new StableEntityId("REQ-002")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "AC 1", RequirementIds = [new StableEntityId("REQ-001")] }
            ]
        };

        WorkSpec candidate = current with
        {
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Keep unchanged", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "Modified statement", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-004"), Statement = "Newly added", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, current, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        Assert.Equal(SpecChangeKind.Unchanged, result.EntityChanges.Single(c_ => c_.EntityId == "CON-001").ChangeKind);
        Assert.Equal(SpecChangeKind.Modified, result.EntityChanges.Single(c_ => c_.EntityId == "CON-002").ChangeKind);
        Assert.Equal(SpecChangeKind.Removed, result.EntityChanges.Single(c_ => c_.EntityId == "CON-003").ChangeKind);
        Assert.Equal(SpecChangeKind.Added, result.EntityChanges.Single(c_ => c_.EntityId == "CON-004").ChangeKind);
    }

    // 9. AcceptanceCriterion Added/Modified/Removed/Unchanged
    [Fact]
    public void AcceptanceCriterion_Added_Modified_Removed_Unchanged_Detected()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec current = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Con 1", RequirementIds = [new StableEntityId("REQ-001")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Keep unchanged", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "To modify", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-003"), Statement = "To remove", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };

        WorkSpec candidate = current with
        {
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "Keep unchanged", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "Modified statement", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-004"), Statement = "Newly added", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, current, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        Assert.Equal(SpecChangeKind.Unchanged, result.EntityChanges.Single(c_ => c_.EntityId == "AC-001").ChangeKind);
        Assert.Equal(SpecChangeKind.Modified, result.EntityChanges.Single(c_ => c_.EntityId == "AC-002").ChangeKind);
        Assert.Equal(SpecChangeKind.Removed, result.EntityChanges.Single(c_ => c_.EntityId == "AC-003").ChangeKind);
        Assert.Equal(SpecChangeKind.Added, result.EntityChanges.Single(c_ => c_.EntityId == "AC-004").ChangeKind);
    }

    // 10. WorkSpec collection reorder is semantic no-op
    [Fact]
    public void WorkSpec_CollectionReorder_IsSemanticNoOp()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec current = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Con 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "Con 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "AC 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "AC 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };

        WorkSpec candidate = current with
        {
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "Con 2", RequirementIds = [new StableEntityId("REQ-002")] },
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Con 1", RequirementIds = [new StableEntityId("REQ-001")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "AC 2", RequirementIds = [new StableEntityId("REQ-002")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "AC 1", RequirementIds = [new StableEntityId("REQ-001")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, current, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.All(result.EntityChanges, c_ => Assert.Equal(SpecChangeKind.Unchanged, c_.ChangeKind));
    }

    // 11. WorkSpec reference-ID reordering is semantic no-op
    [Fact]
    public void WorkSpec_ReferenceId_Reordering_IsSemanticNoOp()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec current = new()
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint
                {
                    Id = new StableEntityId("CON-001"),
                    Statement = "Multi ref constraint",
                    RequirementIds = [new StableEntityId("REQ-001"), new StableEntityId("REQ-002")]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-001"),
                    Statement = "Multi ref AC",
                    RequirementIds = [new StableEntityId("REQ-001"), new StableEntityId("REQ-002")]
                }
            ]
        };

        WorkSpec candidate = current with
        {
            Constraints =
            [
                new Constraint
                {
                    Id = new StableEntityId("CON-001"),
                    Statement = "Multi ref constraint",
                    RequirementIds = [new StableEntityId("REQ-002"), new StableEntityId("REQ-001")]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id = new StableEntityId("AC-001"),
                    Statement = "Multi ref AC",
                    RequirementIds = [new StableEntityId("REQ-002"), new StableEntityId("REQ-001")]
                }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, current, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeWorkSpec(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.All(result.EntityChanges, c_ => Assert.Equal(SpecChangeKind.Unchanged, c_.ChangeKind));
    }

    // 12. PlanStep Added/Modified/Removed/Unchanged
    [Fact]
    public void PlanStep_Added_Modified_Removed_Unchanged_Detected()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan current = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps =
            [
                new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Keep unchanged", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-002"), Statement = "To modify", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-003"), Statement = "To remove", RequirementIds = [new StableEntityId("REQ-002")], AcceptanceCriterionIds = [new StableEntityId("AC-002")] }
            ]
        };

        ImplementationPlan candidate = current with
        {
            Steps =
            [
                new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Keep unchanged", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-002"), Statement = "Modified statement", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-004"), Statement = "Newly added", RequirementIds = [new StableEntityId("REQ-002")], AcceptanceCriterionIds = [new StableEntityId("AC-002")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, current);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        Assert.Equal(SpecChangeKind.Unchanged, result.EntityChanges.Single(c_ => c_.EntityId == "PLAN-STEP-001").ChangeKind);
        Assert.Equal(SpecChangeKind.Modified, result.EntityChanges.Single(c_ => c_.EntityId == "PLAN-STEP-002").ChangeKind);
        Assert.Equal(SpecChangeKind.Removed, result.EntityChanges.Single(c_ => c_.EntityId == "PLAN-STEP-003").ChangeKind);
        Assert.Equal(SpecChangeKind.Added, result.EntityChanges.Single(c_ => c_.EntityId == "PLAN-STEP-004").ChangeKind);
    }

    // 13. PlanStep reference-ID reordering does not modify the step
    [Fact]
    public void PlanStep_ReferenceId_Reordering_DoesNotModifyStep()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan current = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps =
            [
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-001"),
                    Statement = "Multi reference step",
                    RequirementIds = [new StableEntityId("REQ-001"), new StableEntityId("REQ-002")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-001"), new StableEntityId("AC-002")]
                }
            ]
        };

        ImplementationPlan candidate = current with
        {
            Steps =
            [
                new PlanStep
                {
                    Id = new StableEntityId("PLAN-STEP-001"),
                    Statement = "Multi reference step",
                    RequirementIds = [new StableEntityId("REQ-002"), new StableEntityId("REQ-001")],
                    AcceptanceCriterionIds = [new StableEntityId("AC-002"), new StableEntityId("AC-001")]
                }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, current);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.Equal(SpecChangeKind.Unchanged, result.EntityChanges.Single(c_ => c_.EntityId == "PLAN-STEP-001").ChangeKind);
    }

    // 14. Plan step reordering:
    //     * steps remain individually Unchanged;
    //     * OrderingChanged=true;
    //     * artifact SemanticChanged=true.
    [Fact]
    public void PlanStep_Reordering_SetsOrderingChangedTrue_AndSemanticChangedTrue_WithStepsUnchanged()
    {
        RequirementSet rs = CreateRequirementSetMulti();
        WorkSpec ws = CreateWorkSpecMulti();
        ImplementationPlan current = new()
        {
            Revision = new ArtifactRevision(1),
            WorkSpecRevision = new ArtifactRevision(1),
            Steps =
            [
                new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Step 1", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-002"), Statement = "Step 2", RequirementIds = [new StableEntityId("REQ-002")], AcceptanceCriterionIds = [new StableEntityId("AC-002")] }
            ]
        };

        ImplementationPlan candidate = current with
        {
            Steps =
            [
                new PlanStep { Id = new StableEntityId("PLAN-STEP-002"), Statement = "Step 2", RequirementIds = [new StableEntityId("REQ-002")], AcceptanceCriterionIds = [new StableEntityId("AC-002")] },
                new PlanStep { Id = new StableEntityId("PLAN-STEP-001"), Statement = "Step 1", RequirementIds = [new StableEntityId("REQ-001")], AcceptanceCriterionIds = [new StableEntityId("AC-001")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(rs, ws, current);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeImplementationPlan(_specId, snapshot, null, candidate);

        Assert.True(result.SemanticChanged);
        Assert.True(result.OrderingChanged);
        Assert.Equal(2, result.ProposedRevision.Value);
        Assert.All(result.EntityChanges, c_ => Assert.Equal(SpecChangeKind.Unchanged, c_.ChangeKind));
    }

    // 15. Candidate own Revision change alone does not produce semantic change
    [Fact]
    public void Candidate_OwnRevisionChangeAlone_DoesNotProduceSemanticChange()
    {
        RequirementSet current = CreateRequirementSet();
        RequirementSet candidate = current with
        {
            Revision = new ArtifactRevision(99)
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.Equal(current.Revision, result.ProposedRevision);
        Assert.All(result.EntityChanges, c_ => Assert.Equal(SpecChangeKind.Unchanged, c_.ChangeKind));
    }

    // 16. Formatting/property-order changes do not produce semantic change
    [Fact]
    public void Formatting_AndPropertyOrder_DoNotProduceSemanticChange()
    {
        RequirementSet current = CreateRequirementSet();
        string formattedJson = """
        {
            "schemaVersion": 1,
            "schemaId": "ai.repokit.spec",
            "requirements": [
                {
                    "sourceInputIds": [
                        "INPUT-001"
                    ],
                    "statement": "Statement 1",
                    "id": "REQ-001"
                }
            ],
            "inputs": [
                {
                    "text": "Input 1",
                    "id": "INPUT-001"
                }
            ],
            "revision": 1,
            "artifactIdentity": "requirements"
        }
        """;

        RequirementSet candidate = SpecJsonSerializer.Deserialize<RequirementSet>(formattedJson);

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        Assert.False(result.SemanticChanged);
        Assert.False(result.OrderingChanged);
        Assert.Equal(result.CurrentSemanticDigest, result.CandidateSemanticDigest);
    }

    // 17. Deterministic entity ordering
    [Fact]
    public void Deterministic_EntityOrdering_Enforced()
    {
        RequirementSet current = new()
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-003"), Text = "Input 3" },
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Req 2", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Req 1", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };

        RequirementSet candidate = current with
        {
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" },
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult result = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        string[] expectedEntityIds = ["INPUT-001", "INPUT-002", "INPUT-003", "REQ-001", "REQ-002"];
        Assert.Equal(expectedEntityIds, result.EntityChanges.Select(c_ => c_.EntityId).ToArray());
    }

    // 18. Deterministic repeated result
    [Fact]
    public void Deterministic_RepeatedResult_ProducesIdenticalOutputs()
    {
        RequirementSet current = CreateRequirementSet();
        RequirementSet candidate = current with
        {
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Modified", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };

        SpecWorkspaceSnapshot snapshot = SpecWorkspaceValidator.Validate(current, null, null);
        SpecDiffResult first = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);
        SpecDiffResult second = SpecDiffAnalyzer.AnalyzeRequirementSet(_specId, snapshot, null, candidate);

        string firstJson = SpecJsonSerializer.Serialize(first);
        string secondJson = SpecJsonSerializer.Serialize(second);

        Assert.Equal(firstJson, secondJson);
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Statement 1", SourceInputIds = [new StableEntityId("INPUT-001")] }
            ]
        };
    }

    private static RequirementSet CreateRequirementSetMulti()
    {
        return new RequirementSet
        {
            Revision = new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput { Id = new StableEntityId("INPUT-001"), Text = "Input 1" },
                new RequirementInput { Id = new StableEntityId("INPUT-002"), Text = "Input 2" }
            ],
            Requirements =
            [
                new Requirement { Id = new StableEntityId("REQ-001"), Statement = "Statement 1", SourceInputIds = [new StableEntityId("INPUT-001")] },
                new Requirement { Id = new StableEntityId("REQ-002"), Statement = "Statement 2", SourceInputIds = [new StableEntityId("INPUT-002")] }
            ]
        };
    }

    private static WorkSpec CreateWorkSpecMulti()
    {
        return new WorkSpec
        {
            Revision = new ArtifactRevision(1),
            RequirementSetRevision = new ArtifactRevision(1),
            Constraints =
            [
                new Constraint { Id = new StableEntityId("CON-001"), Statement = "Con 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new Constraint { Id = new StableEntityId("CON-002"), Statement = "Con 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion { Id = new StableEntityId("AC-001"), Statement = "AC 1", RequirementIds = [new StableEntityId("REQ-001")] },
                new AcceptanceCriterion { Id = new StableEntityId("AC-002"), Statement = "AC 2", RequirementIds = [new StableEntityId("REQ-002")] }
            ]
        };
    }
}
