using System.Text.Json;
using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecJsonSerializerTests
{
    [Fact]
    public void StableEntityId_SerializesAsString()
    {
        RequirementInput input =
            new()
            {
                Id =
                    new StableEntityId(
                        "INPUT-001"),
                Text =
                    "Example"
            };

        string json =
            SpecJsonSerializer.Serialize(
                input);

        Assert.Equal(
            "{\"id\":\"INPUT-001\",\"text\":\"Example\"}",
            json);
    }

    [Fact]
    public void ArtifactRevision_SerializesAsInteger()
    {
        RequirementSet requirementSet =
            CreateRequirementSet();

        string json =
            SpecJsonSerializer.Serialize(
                requirementSet);

        Assert.Contains(
            "\"revision\":3",
            json,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "\"revision\":{\"value\":3}",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Enum_SerializesAsCamelCaseString()
    {
        Approval approval =
            CreateApproval();

        string json =
            SpecJsonSerializer.Serialize(
                approval);

        Assert.Contains(
            "\"artifactKind\":\"workSpec\"",
            json,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "\"artifactKind\":2",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationStatus_SerializesAsCamelCaseString()
    {
        VerificationResult result =
            new()
            {
                Id =
                    new StableEntityId(
                        "VER-001"),
                AcceptanceCriterionId =
                    new StableEntityId(
                        "AC-001"),
                Status =
                    VerificationStatus.NotVerified,
                EvidenceIds =
                    [],
                Summary =
                    "Pending"
            };

        string json =
            SpecJsonSerializer.Serialize(
                result);

        Assert.Contains(
            "\"status\":\"notVerified\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Serialization_PropertyOrderingIsDeterministic()
    {
        RequirementInput input =
            new()
            {
                Text =
                    "Example",
                Id =
                    new StableEntityId(
                        "INPUT-001")
            };

        string first =
            SpecJsonSerializer.Serialize(
                input);

        string second =
            SpecJsonSerializer.Serialize(
                input);

        Assert.Equal(
            first,
            second);

        Assert.Equal(
            "{\"id\":\"INPUT-001\",\"text\":\"Example\"}",
            first);
    }

    [Fact]
    public void DeserializeThenSerialize_NormalizesWhitespaceAndPropertyOrder()
    {
        string json =
            """
            {
              "text": "Example",
              "id": "INPUT-001"
            }
            """;

        RequirementInput input =
            SpecJsonSerializer.Deserialize<RequirementInput>(
                json);

        string normalized =
            SpecJsonSerializer.Serialize(
                input);

        Assert.Equal(
            "{\"id\":\"INPUT-001\",\"text\":\"Example\"}",
            normalized);
    }

    [Fact]
    public void RequirementSet_RoundTripsDeterministically()
    {
        RequirementSet original =
            CreateRequirementSet();

        string first =
            SpecJsonSerializer.Serialize(
                original);

        RequirementSet restored =
            SpecJsonSerializer.Deserialize<RequirementSet>(
                first);

        string second =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            second);

    }

    [Fact]
    public void WorkSpec_RoundTripsDeterministically()
    {
        WorkSpec original =
            CreateWorkSpec();

        string first =
            SpecJsonSerializer.Serialize(
                original);

        WorkSpec restored =
            SpecJsonSerializer.Deserialize<WorkSpec>(
                first);

        string second =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            second);

    }

    [Fact]
    public void ImplementationPlan_RoundTripsDeterministically()
    {
        ImplementationPlan original =
            CreatePlan();

        string first =
            SpecJsonSerializer.Serialize(
                original);

        ImplementationPlan restored =
            SpecJsonSerializer.Deserialize<ImplementationPlan>(
                first);

        string second =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            second);

    }

    [Fact]
    public void Approval_RoundTripsDeterministically()
    {
        Approval original =
            CreateApproval();

        string first =
            SpecJsonSerializer.Serialize(
                original);

        Approval restored =
            SpecJsonSerializer.Deserialize<Approval>(
                first);

        string second =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            second);

        Assert.Equal(
            original,
            restored);
    }

    [Fact]
    public void VerificationEvidence_RoundTripsDeterministically()
    {
        VerificationEvidence original =
            new()
            {
                Id =
                    new StableEntityId(
                        "EVD-001"),
                Description =
                    "Evidence",
                AcceptanceCriterionIds =
                [
                    new StableEntityId(
                        "AC-001")
                ],
                PlanStepIds =
                [
                    new StableEntityId(
                        "PLAN-STEP-001")
                ]
            };

        string first =
            SpecJsonSerializer.Serialize(
                original);

        VerificationEvidence restored =
            SpecJsonSerializer.Deserialize<VerificationEvidence>(
                first);

        string second =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            second);

    }

    [Fact]
    public void UnknownMember_IsRejected()
    {
        string json =
            """
            {
              "id": "INPUT-001",
              "text": "Example",
              "unexpected": true
            }
            """;

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<RequirementInput>(
                    json));
    }

    [Fact]
    public void IntegerEnum_IsRejected()
    {
        string json =
            """
            {
              "acceptanceCriterionId": "AC-001",
              "evidenceIds": [],
              "id": "VER-001",
              "status": 1,
              "summary": "Result"
            }
            """;

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<VerificationResult>(
                    json));
    }

    [Fact]
    public void InvalidStableEntityId_IsRejectedDuringDeserialization()
    {
        string json =
            """
            {
              "id": "req-001",
              "text": "Example"
            }
            """;

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<RequirementInput>(
                    json));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidArtifactRevision_IsRejectedDuringDeserialization(
        int revision_)
    {
        string json =
            $$"""
            {
              "artifactIdentity": "requirements",
              "inputs": [],
              "requirements": [],
              "revision": {{revision_}},
              "schemaId": "ai.repokit.spec",
              "schemaVersion": 1
            }
            """;

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<RequirementSet>(
                    json));
    }

    private static RequirementSet CreateRequirementSet()
    {
        return new RequirementSet
        {
            Revision =
                new ArtifactRevision(
                    3),
            Inputs =
            [
                new RequirementInput
                {
                    Id =
                        new StableEntityId(
                            "INPUT-001"),
                    Text =
                        "Source input"
                }
            ],
            Requirements =
            [
                new Requirement
                {
                    Id =
                        new StableEntityId(
                            "REQ-001"),
                    Statement =
                        "Requirement",
                    SourceInputIds =
                    [
                        new StableEntityId(
                            "INPUT-001")
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
                new ArtifactRevision(
                    5),
            RequirementSetRevision =
                new ArtifactRevision(
                    3),
            Constraints =
            [
                new Constraint
                {
                    Id =
                        new StableEntityId(
                            "CON-001"),
                    Statement =
                        "Constraint",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ]
                }
            ],
            AcceptanceCriteria =
            [
                new AcceptanceCriterion
                {
                    Id =
                        new StableEntityId(
                            "AC-001"),
                    Statement =
                        "Criterion",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
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
                new ArtifactRevision(
                    2),
            WorkSpecRevision =
                new ArtifactRevision(
                    5),
            Steps =
            [
                new PlanStep
                {
                    Id =
                        new StableEntityId(
                            "PLAN-STEP-001"),
                    Statement =
                        "Implement",
                    RequirementIds =
                    [
                        new StableEntityId(
                            "REQ-001")
                    ],
                    AcceptanceCriterionIds =
                    [
                        new StableEntityId(
                            "AC-001")
                    ]
                }
            ]
        };
    }

    private static Approval CreateApproval()
    {
        return new Approval
        {
            Id =
                new StableEntityId(
                    "APR-001"),
            ArtifactKind =
                SpecArtifactKind.WorkSpec,
            ArtifactIdentity =
                SpecArtifactIdentity.WorkSpec,
            ArtifactRevision =
                new ArtifactRevision(
                    5),
            CanonicalSemanticRepresentation =
                "semantic-v1",
            SemanticDigest =
                new string(
                    'a',
                    64)
        };
    }
}
