using System.Reflection;
using System.Text.Json;
using AiRepoKit.Spec;
using AiRepoKit.Spec.Context;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecContextContractTests
{
    [Fact]
    public void SchemaConstants_AreSeparateAndVersioned()
    {
        Assert.Equal(
            "ai.repokit.spec-context",
            SpecContextSchema.SchemaId);
        Assert.Equal(
            1,
            SpecContextSchema.SchemaVersion);
        Assert.NotEqual(
            SpecSchema.SchemaId,
            SpecContextSchema.SchemaId);
    }

    [Fact]
    public void ValidContext_HasNoValidationErrors()
    {
        Assert.Empty(
            SpecContextValidator.Validate(
                CreateContext()));
    }

    [Fact]
    public void NullContext_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                SpecContextValidator.Validate(
                    null!));
    }

    [Theory]
    [InlineData("Revision")]
    [InlineData("ArtifactIdentity")]
    public void SpecContext_DoesNotExposeCanonicalArtifactProperties(
        string propertyName_)
    {
        Assert.Null(
            typeof(SpecContext).GetProperty(
                propertyName_,
                BindingFlags.Instance |
                BindingFlags.Public));
    }

    [Fact]
    public void SpecSemanticDigest_HasNoSpecContextOverload()
    {
        Assert.DoesNotContain(
            typeof(SpecSemanticDigest).GetMethods(
                BindingFlags.Public |
                BindingFlags.Static),
            method_ =>
                method_.GetParameters().Any(
                    parameter_ =>
                        parameter_.ParameterType ==
                        typeof(SpecContext)));
    }

    [Fact]
    public void Serialization_IsDeterministicAndRoundTripsCompleteContext()
    {
        SpecContext original =
            CreateContext() with
            {
                Target =
                    "src",
                Truncated =
                    true,
                Omissions =
                [
                    new SpecContextOmission
                    {
                        Reference =
                            "docs/omitted.md",
                        Reason =
                            "Budget",
                        RemovedEstimatedTokens =
                            17
                    }
                ]
            };

        string first =
            SpecJsonSerializer.Serialize(
                original);
        string repeated =
            SpecJsonSerializer.Serialize(
                original);
        SpecContext restored =
            SpecJsonSerializer.Deserialize<SpecContext>(
                first);
        string roundTrip =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            repeated);
        Assert.Equal(
            first,
            roundTrip);
        Assert.Equal(
            original.SchemaId,
            restored.SchemaId);
        Assert.Equal(
            original.SchemaVersion,
            restored.SchemaVersion);
        Assert.Equal(
            original.SpecId,
            restored.SpecId);
        Assert.Equal(
            original.RequirementSetRevision,
            restored.RequirementSetRevision);
        Assert.Equal(
            original.WorkSpecRevision,
            restored.WorkSpecRevision);
        Assert.Equal(
            original.Target,
            restored.Target);
        Assert.Equal(
            original.ReferenceLimit,
            restored.ReferenceLimit);
        Assert.Equal(
            original.Budget,
            restored.Budget);
        Assert.Equal(
            original.EstimatedTokens,
            restored.EstimatedTokens);
        Assert.Equal(
            original.Truncated,
            restored.Truncated);
        Assert.Equal(
            original.Evidence.ToArray(),
            restored.Evidence.ToArray());
        Assert.Equal(
            original.References.ToArray(),
            restored.References.ToArray());
        Assert.Equal(
            original.Omissions.ToArray(),
            restored.Omissions.ToArray());
        Assert.StartsWith(
            "{\"budget\":",
            first,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Enums_SerializeAsCamelCaseStrings()
    {
        string json =
            SpecJsonSerializer.Serialize(
                CreateContext());

        Assert.Contains(
            "\"availability\":\"available\"",
            json,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"freshness\":\"current\"",
            json,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IntegerEnum_IsRejectedDuringDeserialization()
    {
        string json =
            SpecJsonSerializer.Serialize(
                CreateContext())
                .Replace(
                    "\"availability\":\"available\"",
                    "\"availability\":1",
                    StringComparison.Ordinal);

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<SpecContext>(
                    json));
    }

    [Fact]
    public void UnknownJsonMember_IsRejectedDuringDeserialization()
    {
        string json =
            SpecJsonSerializer.Serialize(
                CreateContext());
        json =
            json.Insert(
                json.Length - 1,
                ",\"unexpected\":true");

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<SpecContext>(
                    json));
    }

    [Fact]
    public void UnsupportedSchemaId_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                SchemaId =
                    "other"
            },
            SpecContextValidationErrorCodes.UnsupportedSpecContextSchema);
    }

    [Fact]
    public void UnsupportedSchemaVersion_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                SchemaVersion =
                    2
            },
            SpecContextValidationErrorCodes.UnsupportedSpecContextSchema);
    }

    [Fact]
    public void InvalidSpecId_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                SpecId =
                    "INVALID"
            },
            SpecContextValidationErrorCodes.InvalidSpecId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidArtifactRevision_IsRejected(
        bool requirementSet_)
    {
        SpecContext context =
            requirementSet_
                ? CreateContext() with
                {
                    RequirementSetRevision =
                        default
                }
                : CreateContext() with
                {
                    WorkSpecRevision =
                        default
                };

        AssertHasCode(
            context,
            SpecContextValidationErrorCodes.InvalidSpecContextRevision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void InvalidReferenceLimit_IsRejected(
        int referenceLimit_)
    {
        AssertHasCode(
            CreateContext() with
            {
                ReferenceLimit =
                    referenceLimit_
            },
            SpecContextValidationErrorCodes.InvalidSpecContextReferenceLimit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveBudget_IsRejected(
        int budget_)
    {
        AssertHasCode(
            CreateContext() with
            {
                Budget =
                    budget_
            },
            SpecContextValidationErrorCodes.InvalidSpecContextBudget);
    }

    [Fact]
    public void NegativeEstimatedTokens_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                EstimatedTokens =
                    -1
            },
            SpecContextValidationErrorCodes.InvalidSpecContextTokenEstimate);
    }

    [Theory]
    [InlineData("evidenceId")]
    [InlineData("source")]
    [InlineData("kind")]
    [InlineData("reference")]
    public void BlankEvidenceField_IsRejected(
        string field_)
    {
        RepositoryEvidence evidence =
            CreateEvidence() with
            {
                EvidenceId =
                    field_ == "evidenceId" ? " " : "evidence-1",
                Source =
                    field_ == "source" ? " " : "repository",
                Kind =
                    field_ == "kind" ? " " : "file",
                Reference =
                    field_ == "reference" ? " " : "README.md"
            };

        AssertHasCode(
            WithEvidence(
                evidence),
            SpecContextValidationErrorCodes.InvalidRepositoryEvidence);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void ZeroOrUndefinedAvailability_IsRejected(
        int availability_)
    {
        AssertHasCode(
            WithEvidence(
                CreateEvidence() with
                {
                    Availability =
                        (RepositoryEvidenceAvailability)availability_
                }),
            SpecContextValidationErrorCodes.InvalidRepositoryEvidenceState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void ZeroOrUndefinedFreshness_IsRejected(
        int freshness_)
    {
        AssertHasCode(
            WithEvidence(
                CreateEvidence() with
                {
                    Freshness =
                        (RepositoryEvidenceFreshness)freshness_
                }),
            SpecContextValidationErrorCodes.InvalidRepositoryEvidenceState);
    }

    [Fact]
    public void DuplicateEvidenceId_IsRejectedUsingOrdinalIdentity()
    {
        AssertHasCode(
            CreateContext() with
            {
                Evidence =
                [
                    CreateEvidence(),
                    CreateEvidence() with
                    {
                        Reference =
                            "other.md"
                    }
                ]
            },
            SpecContextValidationErrorCodes.DuplicateRepositoryEvidenceId);
    }

    [Fact]
    public void EvidenceIdsDifferingOnlyByCase_AreDistinct()
    {
        SpecContext context =
            CreateContext() with
            {
                Evidence =
                [
                    CreateEvidence(),
                    CreateEvidence() with
                    {
                        EvidenceId =
                            "EVIDENCE-1",
                        Reference =
                            "other.md"
                    }
                ]
            };

        Assert.DoesNotContain(
            SpecContextValidator.Validate(
                context),
            error_ =>
                error_.Code ==
                SpecContextValidationErrorCodes.DuplicateRepositoryEvidenceId);
    }

    [Theory]
    [InlineData(RepositoryEvidenceAvailability.Missing, RepositoryEvidenceFreshness.Current)]
    [InlineData(RepositoryEvidenceAvailability.Missing, RepositoryEvidenceFreshness.Stale)]
    [InlineData(RepositoryEvidenceAvailability.Unavailable, RepositoryEvidenceFreshness.Current)]
    [InlineData(RepositoryEvidenceAvailability.Unavailable, RepositoryEvidenceFreshness.Stale)]
    public void MissingOrUnavailableEvidence_CannotClaimCurrentOrStale(
        RepositoryEvidenceAvailability availability_,
        RepositoryEvidenceFreshness freshness_)
    {
        AssertHasCode(
            WithEvidence(
                CreateEvidence() with
                {
                    Availability =
                        availability_,
                    Freshness =
                        freshness_
                }),
            SpecContextValidationErrorCodes.InvalidRepositoryEvidenceState);
    }

    [Theory]
    [InlineData(RepositoryEvidenceAvailability.Missing, RepositoryEvidenceFreshness.Unknown)]
    [InlineData(RepositoryEvidenceAvailability.Missing, RepositoryEvidenceFreshness.NotApplicable)]
    [InlineData(RepositoryEvidenceAvailability.Unavailable, RepositoryEvidenceFreshness.Unknown)]
    [InlineData(RepositoryEvidenceAvailability.Unavailable, RepositoryEvidenceFreshness.NotApplicable)]
    public void MissingOrUnavailableEvidence_AcceptsUnknownOrNotApplicable(
        RepositoryEvidenceAvailability availability_,
        RepositoryEvidenceFreshness freshness_)
    {
        SpecContext context =
            WithEvidence(
                CreateEvidence() with
                {
                    Availability =
                        availability_,
                    Freshness =
                        freshness_
                });

        Assert.Empty(
            SpecContextValidator.Validate(
                context));
    }

    [Theory]
    [InlineData(RepositoryEvidenceFreshness.Current)]
    [InlineData(RepositoryEvidenceFreshness.Stale)]
    [InlineData(RepositoryEvidenceFreshness.Unknown)]
    [InlineData(RepositoryEvidenceFreshness.NotApplicable)]
    public void AvailableEvidence_AcceptsEveryDefinedFreshness(
        RepositoryEvidenceFreshness freshness_)
    {
        Assert.Empty(
            SpecContextValidator.Validate(
                WithEvidence(
                    CreateEvidence() with
                    {
                        Freshness =
                            freshness_
                    })));
    }

    [Fact]
    public void ReferenceToUnknownEvidence_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                References =
                [
                    CreateReference() with
                    {
                        EvidenceId =
                            "missing"
                    }
                ]
            },
            SpecContextValidationErrorCodes.MissingRepositoryEvidenceReference);
    }

    [Theory]
    [InlineData(RepositoryEvidenceAvailability.Missing)]
    [InlineData(RepositoryEvidenceAvailability.Unavailable)]
    public void ReferenceToNonavailableEvidence_IsRejected(
        RepositoryEvidenceAvailability availability_)
    {
        RepositoryEvidence evidence =
            CreateEvidence() with
            {
                Availability =
                    availability_,
                Freshness =
                    RepositoryEvidenceFreshness.Unknown
            };

        AssertHasCode(
            CreateContext() with
            {
                Evidence =
                    [evidence]
            },
            SpecContextValidationErrorCodes.UnavailableRepositoryEvidenceReference);
    }

    [Theory]
    [InlineData("evidenceId")]
    [InlineData("kind")]
    [InlineData("reference")]
    [InlineData("reason")]
    public void BlankReferenceField_IsRejected(
        string field_)
    {
        SpecContextReference reference =
            CreateReference() with
            {
                EvidenceId =
                    field_ == "evidenceId" ? " " : "evidence-1",
                Kind =
                    field_ == "kind" ? " " : "file",
                Reference =
                    field_ == "reference" ? " " : "README.md",
                Reason =
                    field_ == "reason" ? " " : "Relevant"
            };

        AssertHasCode(
            CreateContext() with
            {
                References =
                    [reference]
            },
            SpecContextValidationErrorCodes.InvalidSpecContextReference);
    }

    [Fact]
    public void NegativeReferencePriority_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                References =
                [
                    CreateReference() with
                    {
                        Priority =
                            -1
                    }
                ]
            },
            SpecContextValidationErrorCodes.InvalidSpecContextReference);
    }

    [Fact]
    public void DuplicateReferenceIdentity_IsRejectedOrdinally()
    {
        AssertHasCode(
            CreateContext() with
            {
                References =
                [
                    CreateReference(),
                    CreateReference() with
                    {
                        Reason =
                            "Also relevant"
                    }
                ]
            },
            SpecContextValidationErrorCodes.DuplicateSpecContextReference);
    }

    [Fact]
    public void ReferencesDifferingOnlyByCase_AreDistinct()
    {
        SpecContext context =
            CreateContext() with
            {
                References =
                [
                    CreateReference(),
                    CreateReference() with
                    {
                        Kind =
                            "File",
                        Reference =
                            "readme.md"
                    }
                ]
            };

        Assert.Empty(
            SpecContextValidator.Validate(
                context));
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("reason")]
    public void BlankOmissionField_IsRejected(
        string field_)
    {
        AssertHasCode(
            CreateContext() with
            {
                Truncated =
                    true,
                Omissions =
                [
                    new SpecContextOmission
                    {
                        Reference =
                            field_ == "reference" ? " " : "other.md",
                        Reason =
                            field_ == "reason" ? " " : "Budget"
                    }
                ]
            },
            SpecContextValidationErrorCodes.InvalidSpecContextOmission);
    }

    [Fact]
    public void NegativeRemovedEstimatedTokens_IsRejected()
    {
        AssertHasCode(
            CreateContext() with
            {
                Truncated =
                    true,
                Omissions =
                [
                    new SpecContextOmission
                    {
                        Reference =
                            "other.md",
                        Reason =
                            "Budget",
                        RemovedEstimatedTokens =
                            -1
                    }
                ]
            },
            SpecContextValidationErrorCodes.InvalidSpecContextOmission);
    }

    [Fact]
    public void OmissionsRequireTruncated()
    {
        AssertHasCode(
            ContextWithOmission(
                false),
            SpecContextValidationErrorCodes.InvalidSpecContextTruncation);
    }

    [Fact]
    public void OmissionsAreValidWhenTruncated()
    {
        Assert.Empty(
            SpecContextValidator.Validate(
                ContextWithOmission(
                    true)));
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("references")]
    [InlineData("omissions")]
    public void NullCollections_AreRejectedWithoutThrowing(
        string collection_)
    {
        SpecContext context =
            CreateContext() with
            {
                Evidence =
                    collection_ == "evidence" ? null! : CreateContext().Evidence,
                References =
                    collection_ == "references" ? null! : CreateContext().References,
                Omissions =
                    collection_ == "omissions" ? null! : CreateContext().Omissions
            };

        Assert.NotEmpty(
            SpecContextValidator.Validate(
                context));
    }

    private static SpecContext ContextWithOmission(
        bool truncated_)
    {
        return CreateContext() with
        {
            Truncated =
                truncated_,
            Omissions =
            [
                new SpecContextOmission
                {
                    Reference =
                        "other.md",
                    Reason =
                        "Budget",
                    RemovedEstimatedTokens =
                        10
                }
            ]
        };
    }

    private static SpecContext WithEvidence(
        RepositoryEvidence evidence_)
    {
        return CreateContext() with
        {
            Evidence =
                [evidence_],
            References =
                []
        };
    }

    private static void AssertHasCode(
        SpecContext context_,
        string code_)
    {
        Assert.Contains(
            SpecContextValidator.Validate(
                context_),
            error_ =>
                error_.Code ==
                code_);
    }

    private static SpecContext CreateContext()
    {
        return new SpecContext
        {
            SpecId =
                "example-spec",
            RequirementSetRevision =
                new ArtifactRevision(
                    2),
            WorkSpecRevision =
                new ArtifactRevision(
                    3),
            Target =
                "repository",
            ReferenceLimit =
                10,
            Budget =
                1000,
            EstimatedTokens =
                100,
            Evidence =
                [CreateEvidence()],
            References =
                [CreateReference()],
            Omissions =
                []
        };
    }

    private static RepositoryEvidence CreateEvidence()
    {
        return new RepositoryEvidence
        {
            EvidenceId =
                "evidence-1",
            Source =
                "repository",
            Kind =
                "file",
            Reference =
                "README.md",
            Availability =
                RepositoryEvidenceAvailability.Available,
            Freshness =
                RepositoryEvidenceFreshness.Current,
            SourceGeneratedAt =
                "2026-09-03T00:00:00Z",
            Detail =
                "Repository overview"
        };
    }

    private static SpecContextReference CreateReference()
    {
        return new SpecContextReference
        {
            EvidenceId =
                "evidence-1",
            Kind =
                "file",
            Reference =
                "README.md",
            Reason =
                "Relevant",
            Priority =
                1
        };
    }
}
