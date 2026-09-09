using System.Reflection;
using System.Text.Json;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Persistence;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecApprovalLedgerTests
{
    [Fact]
    public void Validate_AcceptsValidEmptyLedger()
    {
        SpecApprovalLedger ledger =
            CreateLedger(
                []);

        Assert.Empty(
            SpecApprovalLedgerValidator.Validate(
                ledger));

        Assert.Empty(
            SpecApprovalLedgerValidator.Validate(
                ledger,
                new SpecId("spec-sample")));
    }

    [Fact]
    public void Validate_AcceptsLedgerWithRequirementSetApproval()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);

        SpecApprovalLedger ledger =
            CreateLedger(
                [approval]);

        Assert.Empty(
            SpecApprovalLedgerValidator.Validate(
                ledger));
    }

    [Fact]
    public void Validate_AcceptsLedgerWithWorkSpecApproval()
    {
        WorkSpec workSpec =
            CreateWorkSpec();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                workSpec);

        SpecApprovalLedger ledger =
            CreateLedger(
                [approval]);

        Assert.Empty(
            SpecApprovalLedgerValidator.Validate(
                ledger));
    }

    [Fact]
    public void Validate_AcceptsLedgerWithImplementationPlanApproval()
    {
        ImplementationPlan plan =
            CreatePlan();

        Approval approval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-003"),
                plan);

        SpecApprovalLedger ledger =
            CreateLedger(
                [approval]);

        Assert.Empty(
            SpecApprovalLedgerValidator.Validate(
                ledger));
    }

    [Fact]
    public void Serialization_RepeatedSerializationIsDeterministic()
    {
        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-001"),
                        CreateRequirementSet()),
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-002"),
                        CreateWorkSpec()),
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-003"),
                        CreatePlan())
                ]);

        string first =
            SpecJsonSerializer.Serialize(
                ledger);

        for (int i = 0; i < 5; i++)
        {
            string repeated =
                SpecJsonSerializer.Serialize(
                    ledger);

            Assert.Equal(
                first,
                repeated);
        }
    }

    [Fact]
    public void Serialization_ApprovalListOrderingPreservedInJsonAndRoundTrip()
    {
        Approval approval1 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-003"),
                CreatePlan());
        Approval approval2 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                CreateRequirementSet());
        Approval approval3 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                CreateWorkSpec());

        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    approval1,
                    approval2,
                    approval3
                ]);

        string json =
            SpecJsonSerializer.Serialize(
                ledger);

        int index1 =
            json.IndexOf("APR-003", StringComparison.Ordinal);
        int index2 =
            json.IndexOf("APR-001", StringComparison.Ordinal);
        int index3 =
            json.IndexOf("APR-002", StringComparison.Ordinal);

        Assert.True(index1 >= 0);
        Assert.True(index2 >= 0);
        Assert.True(index3 >= 0);
        Assert.True(index1 < index2);
        Assert.True(index2 < index3);

        SpecApprovalLedger restored =
            SpecJsonSerializer.Deserialize<SpecApprovalLedger>(
                json);

        Assert.Equal(3, restored.Approvals.Count);
        Assert.Equal("APR-003", restored.Approvals[0].Id.Value);
        Assert.Equal("APR-001", restored.Approvals[1].Id.Value);
        Assert.Equal("APR-002", restored.Approvals[2].Id.Value);
    }

    [Fact]
    public void Validate_RejectsDuplicateApprovalId()
    {
        Approval approval1 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                CreateRequirementSet());
        Approval approval2 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                CreateWorkSpec());

        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    approval1,
                    approval2
                ]);

        IReadOnlyList<SpecValidationError> errors =
            SpecApprovalLedgerValidator.Validate(
                ledger);

        SpecValidationError error =
            Assert.Single(
                errors);

        Assert.Equal(
            SpecValidationErrorCodes.DuplicateEntityId,
            error.Code);
        Assert.Equal(
            "APR-001",
            error.SourceEntityId);
    }

    [Fact]
    public void Validate_RejectsInvalidApprovalIdentityThroughExistingApprovalValidator()
    {
        Approval approval =
            new()
            {
                Id =
                    new StableEntityId(
                        "REQ-001"),
                ArtifactKind =
                    SpecArtifactKind.RequirementSet,
                ArtifactIdentity =
                    SpecArtifactIdentity.RequirementSet,
                ArtifactRevision =
                    new ArtifactRevision(1),
                CanonicalSemanticRepresentation =
                    "req-v1",
                SemanticDigest =
                    new string('a', 64)
            };

        SpecApprovalLedger ledger =
            CreateLedger(
                [approval]);

        IReadOnlyList<SpecValidationError> errors =
            SpecApprovalLedgerValidator.Validate(
                ledger);

        SpecValidationError error =
            Assert.Single(
                errors);

        Assert.Equal(
            SpecValidationErrorCodes.InvalidEntityKind,
            error.Code);
        Assert.Equal(
            "REQ-001",
            error.SourceEntityId);
    }

    [Fact]
    public void Validate_RejectsInvalidLedgerSchemaId()
    {
        SpecApprovalLedger ledger =
            CreateLedger([]) with
            {
                SchemaId =
                    "unsupported.schema"
            };

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecValidationErrorCodes.UnsupportedSchemaId,
            error.Code);
    }

    [Fact]
    public void Validate_RejectsInvalidLedgerSchemaVersion()
    {
        SpecApprovalLedger ledger =
            CreateLedger([]) with
            {
                SchemaVersion =
                    99
            };

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecValidationErrorCodes.UnsupportedSchemaVersion,
            error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-invalid-prefix")]
    [InlineData("invalid-suffix-")]
    [InlineData("HAS_UPPERCASE")]
    [InlineData("has_underscore")]
    [InlineData("con")]
    [InlineData("aux")]
    [InlineData("nul")]
    public void Validate_RejectsInvalidLedgerSpecId(
        string invalidSpecId_)
    {
        SpecApprovalLedger ledger =
            CreateLedger([]) with
            {
                SpecId =
                    invalidSpecId_
            };

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecApprovalLedgerValidationErrorCodes.InvalidSpecId,
            error.Code);
    }

    [Fact]
    public void Validate_RejectsExpectedWorkspaceSpecIdMismatch()
    {
        SpecApprovalLedger ledger =
            CreateLedger([]) with
            {
                SpecId =
                    "feature-alpha"
            };

        SpecId expectedSpecId =
            new("feature-beta");

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger,
                    expectedSpecId));

        Assert.Equal(
            SpecApprovalLedgerValidationErrorCodes.SpecIdMismatch,
            error.Code);
        Assert.Equal(
            "feature-alpha",
            error.SourceEntityId);
        Assert.Equal(
            "feature-beta",
            error.TargetEntityId);
    }

    [Fact]
    public void Validate_PropagatesApprovalValidatorErrorForInvalidSemanticDigest()
    {
        Approval invalidApproval =
            new()
            {
                Id =
                    new StableEntityId("APR-001"),
                ArtifactKind =
                    SpecArtifactKind.RequirementSet,
                ArtifactIdentity =
                    SpecArtifactIdentity.RequirementSet,
                ArtifactRevision =
                    new ArtifactRevision(1),
                CanonicalSemanticRepresentation =
                    "representation",
                SemanticDigest =
                    "invalid-digest"
            };

        SpecApprovalLedger ledger =
            CreateLedger([invalidApproval]);

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidSemanticDigest,
            error.Code);
        Assert.Equal(
            "APR-001",
            error.SourceEntityId);
    }

    [Fact]
    public void Serialization_DeterministicDeserializeSerializeRoundTrip()
    {
        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-001"),
                        CreateRequirementSet()),
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-002"),
                        CreateWorkSpec())
                ]);

        string firstJson =
            SpecJsonSerializer.Serialize(
                ledger);

        SpecApprovalLedger restored =
            SpecJsonSerializer.Deserialize<SpecApprovalLedger>(
                firstJson);

        string secondJson =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            firstJson,
            secondJson);

        Assert.Equal(ledger.SpecId, restored.SpecId);
        Assert.Equal(ledger.Revision, restored.Revision);
        Assert.Equal(ledger.ArtifactIdentity, restored.ArtifactIdentity);
        Assert.Equal(ledger.SchemaId, restored.SchemaId);
        Assert.Equal(ledger.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(ledger.Approvals.Count, restored.Approvals.Count);
    }

    [Fact]
    public void CanonicalLedgerJson_ContainsNoDerivedStatusFieldOrValue()
    {
        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    SpecApprovalBinding.Create(
                        new StableEntityId("APR-001"),
                        CreateRequirementSet())
                ]);

        string json =
            SpecJsonSerializer.Serialize(
                ledger);

        using JsonDocument document =
            JsonDocument.Parse(
                json);

        AssertNoStatusProperties(
            document.RootElement);

        Assert.DoesNotContain(
            "\"status\"",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "notApproved",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "current",
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "stale",
            json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Contracts_DoNotContainClientModelMachineOrTimestampFields()
    {
        string[] prohibitedSubstrings =
        [
            "client",
            "model",
            "machine",
            "timestamp",
            "createdat",
            "updatedat",
            "time",
            "date",
            "actor",
            "user",
            "path"
        ];

        PropertyInfo[] ledgerProperties =
            typeof(SpecApprovalLedger).GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

        string[] ledgerPropertyNames =
            ledgerProperties.Select(p => p.Name).ToArray();

        Assert.Equal(
            new[]
            {
                nameof(SpecApprovalLedger.Approvals),
                nameof(SpecApprovalLedger.ArtifactIdentity),
                nameof(SpecApprovalLedger.Revision),
                nameof(SpecApprovalLedger.SchemaId),
                nameof(SpecApprovalLedger.SchemaVersion),
                nameof(SpecApprovalLedger.SpecId)
            }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ledgerPropertyNames.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        foreach (string name in ledgerPropertyNames)
        {
            foreach (string prohibited in prohibitedSubstrings)
            {
                Assert.DoesNotContain(
                    prohibited,
                    name,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        PropertyInfo[] artifactStatusProperties =
            typeof(SpecArtifactApprovalStatus).GetProperties(
                BindingFlags.Public | BindingFlags.Instance);

        string[] artifactStatusPropertyNames =
            artifactStatusProperties.Select(p => p.Name).ToArray();

        Assert.Equal(
            new[]
            {
                nameof(SpecArtifactApprovalStatus.ArtifactIdentity),
                nameof(SpecArtifactApprovalStatus.ArtifactKind),
                nameof(SpecArtifactApprovalStatus.Status)
            }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            artifactStatusPropertyNames.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        string[] statusEnumNames =
            Enum.GetNames<SpecApprovalStatus>();

        Assert.Equal(
            new[]
            {
                nameof(SpecApprovalStatus.Current),
                nameof(SpecApprovalStatus.NotApproved),
                nameof(SpecApprovalStatus.Stale)
            }.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            statusEnumNames.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Validate_RejectsInvalidLedgerRevision()
    {
        SpecApprovalLedger ledger =
            CreateLedger([]) with
            {
                Revision =
                    default
            };

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecValidationErrorCodes.InvalidRevision,
            error.Code);
    }

    [Fact]
    public void Validate_RejectsArtifactIdentityOtherThanApprovals()
    {
        SpecApprovalLedger ledger =
            CreateLedger([]) with
            {
                ArtifactIdentity =
                    "not-approvals"
            };

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecValidationErrorCodes.ArtifactIdentityMismatch,
            error.Code);
        Assert.Equal(
            "not-approvals",
            error.TargetEntityId);
    }

    [Fact]
    public void Validate_AcceptsOlderRevisionApprovalAsValidHistory()
    {
        // An approval created for an earlier revision is valid ledger history and must not be rejected.
        RequirementSet oldRevisionSet =
            new()
            {
                Revision =
                    new ArtifactRevision(1),
                Inputs =
                [
                    new RequirementInput
                    {
                        Id =
                            new StableEntityId("INPUT-001"),
                        Text =
                            "Initial input"
                    }
                ],
                Requirements =
                [
                    new Requirement
                    {
                        Id =
                            new StableEntityId("REQ-001"),
                        Statement =
                            "Initial statement",
                        SourceInputIds =
                        [
                            new StableEntityId("INPUT-001")
                        ]
                    }
                ]
            };

        Approval oldApproval =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                oldRevisionSet);

        // Ledger with revision 2 containing historical revision 1 approval
        SpecApprovalLedger ledger =
            new()
            {
                SpecId =
                    "spec-history",
                Revision =
                    new ArtifactRevision(2),
                Approvals =
                [
                    oldApproval
                ]
            };

        Assert.Empty(
            SpecApprovalLedgerValidator.Validate(
                ledger));
    }

    [Fact]
    public void SpecArtifactApprovalStatus_ConstructsCorrectly()
    {
        SpecArtifactApprovalStatus status =
            new()
            {
                ArtifactKind =
                    SpecArtifactKind.WorkSpec,
                ArtifactIdentity =
                    SpecArtifactIdentity.WorkSpec,
                Status =
                    SpecApprovalStatus.Current
            };

        Assert.Equal(
            SpecArtifactKind.WorkSpec,
            status.ArtifactKind);
        Assert.Equal(
            SpecArtifactIdentity.WorkSpec,
            status.ArtifactIdentity);
        Assert.Equal(
            SpecApprovalStatus.Current,
            status.Status);
    }

    [Fact]
    public void Validate_ThrowsOnNullLedger()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                SpecApprovalLedgerValidator.Validate(
                    null!));
    }

    [Fact]
    public void Validate_RejectsNullApprovalEntry()
    {
        SpecApprovalLedger ledger =
            CreateLedger(
                [null!]);

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecApprovalLedgerValidationErrorCodes.NullApprovalEntry,
            error.Code);
    }

    [Fact]
    public void Validate_RejectsInconsistentSemanticDigest()
    {
        Approval approval =
            new()
            {
                Id =
                    new StableEntityId("APR-001"),
                ArtifactKind =
                    SpecArtifactKind.RequirementSet,
                ArtifactIdentity =
                    SpecArtifactIdentity.RequirementSet,
                ArtifactRevision =
                    new ArtifactRevision(1),
                CanonicalSemanticRepresentation =
                    "some-canonical-representation",
                SemanticDigest =
                    new string('f', 64)
            };

        SpecApprovalLedger ledger =
            CreateLedger(
                [approval]);

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecApprovalLedgerValidationErrorCodes.InconsistentSemanticDigest,
            error.Code);
        Assert.Equal(
            "APR-001",
            error.SourceEntityId);
    }

    [Fact]
    public void Validate_RejectsDuplicateApprovalBinding()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();

        Approval approval1 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                requirementSet);
        Approval approval2 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                requirementSet);

        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    approval1,
                    approval2
                ]);

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecApprovalLedgerValidationErrorCodes.DuplicateApprovalBinding,
            error.Code);
        Assert.Equal(
            "APR-002",
            error.SourceEntityId);
        Assert.Equal(
            "APR-001",
            error.TargetEntityId);
    }

    [Fact]
    public void Validate_RejectsConflictingApprovalBinding()
    {
        RequirementSet set1 =
            CreateRequirementSet();
        RequirementSet set2 =
            CreateRequirementSet() with
            {
                Requirements =
                [
                    new Requirement
                    {
                        Id =
                            new StableEntityId("REQ-002"),
                        Statement =
                            "Different statement",
                        SourceInputIds =
                        [
                            new StableEntityId("INPUT-001")
                        ]
                    }
                ]
            };

        Approval approval1 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-001"),
                set1);
        Approval approval2 =
            SpecApprovalBinding.Create(
                new StableEntityId("APR-002"),
                set2);

        SpecApprovalLedger ledger =
            CreateLedger(
                [
                    approval1,
                    approval2
                ]);

        SpecValidationError error =
            Assert.Single(
                SpecApprovalLedgerValidator.Validate(
                    ledger));

        Assert.Equal(
            SpecApprovalLedgerValidationErrorCodes.ConflictingApprovalBinding,
            error.Code);
        Assert.Equal(
            "APR-002",
            error.SourceEntityId);
        Assert.Equal(
            "APR-001",
            error.TargetEntityId);
    }

    private static void AssertNoStatusProperties(
        JsonElement element_)
    {
        if (element_.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element_.EnumerateObject())
            {
                Assert.False(
                    string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase),
                    $"JSON object contains unexpected status property: '{property.Name}'.");
                Assert.False(
                    string.Equals(property.Name, "current", StringComparison.OrdinalIgnoreCase),
                    $"JSON object contains unexpected current property: '{property.Name}'.");
                Assert.False(
                    string.Equals(property.Name, "stale", StringComparison.OrdinalIgnoreCase),
                    $"JSON object contains unexpected stale property: '{property.Name}'.");
                Assert.False(
                    string.Equals(property.Name, "notApproved", StringComparison.OrdinalIgnoreCase),
                    $"JSON object contains unexpected notApproved property: '{property.Name}'.");

                AssertNoStatusProperties(
                    property.Value);
            }
        }
        else if (element_.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element_.EnumerateArray())
            {
                AssertNoStatusProperties(
                    item);
            }
        }
    }

    private static SpecApprovalLedger CreateLedger(
        IReadOnlyList<Approval> approvals_)
    {
        return new SpecApprovalLedger
        {
            SpecId =
                "spec-sample",
            Approvals =
                approvals_
        };
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(1),
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId("INPUT-001"),
                    Text =
                        "Source input"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId("REQ-001"),
                    Statement =
                        "Requirement",
                    SourceInputIds =
                    [
                        new StableEntityId("INPUT-001")
                    ]
                }
            ]
        };
    }

    private static WorkSpec CreateWorkSpec()
    {
        return new WorkSpec
        {
            Revision =
                new ArtifactRevision(1),
            RequirementSetRevision =
                new ArtifactRevision(1),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId("CON-001"),
                    Statement =
                        "Constraint",
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
                        "Criterion",
                    RequirementIds =
                    [
                        new StableEntityId("REQ-001")
                    ]
                }
            ]
        };
    }

    private static ImplementationPlan CreatePlan()
    {
        return new ImplementationPlan
        {
            Revision =
                new ArtifactRevision(1),
            WorkSpecRevision =
                new ArtifactRevision(1),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId("PLAN-STEP-001"),
                    Statement =
                        "Implement",
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
}
