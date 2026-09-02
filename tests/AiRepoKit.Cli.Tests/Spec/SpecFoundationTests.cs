using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecFoundationTests
{
    [Fact]
    public void SchemaContract_IsExplicitAndVersioned()
    {
        Assert.Equal(
            "ai.repokit.spec",
            SpecSchema.SchemaId);

        Assert.Equal(
            1,
            SpecSchema.SchemaVersion);

        Assert.Equal(
            "ai.repokit.spec.semantic",
            SpecSchema.CanonicalizationId);

        Assert.Equal(
            1,
            SpecSchema.CanonicalizationVersion);

        Assert.Equal(
            "sha256",
            SpecSchema.DigestAlgorithm);
    }

    [Fact]
    public void ArtifactKinds_KeepRequirementSetAndWorkSpecDistinct()
    {
        Assert.NotEqual(
            SpecArtifactKind.RequirementSet,
            SpecArtifactKind.WorkSpec);
    }

    [Theory]
    [InlineData("REQ-001")]
    [InlineData("AC-001")]
    [InlineData("PLAN-STEP-001")]
    [InlineData("EVD-001")]
    [InlineData("REQ-0001")]
    public void StableEntityId_AcceptsCanonicalForms(
        string value_)
    {
        Assert.True(
            StableEntityId.TryParse(
                value_,
                out StableEntityId entityId));

        Assert.Equal(
            value_,
            entityId.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("req-001")]
    [InlineData("Req-001")]
    [InlineData("REQ-01")]
    [InlineData("REQ_001")]
    [InlineData("REQ-ABC")]
    [InlineData("-001")]
    [InlineData("REQ-")]
    [InlineData(" REQ-001")]
    [InlineData("REQ-001 ")]
    public void StableEntityId_RejectsNonCanonicalForms(
        string? value_)
    {
        Assert.False(
            StableEntityId.TryParse(
                value_,
                out _));
    }

    [Fact]
    public void StableEntityId_ConstructorRejectsInvalidIdentity()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new StableEntityId(
                    "req-001"));
    }

    [Fact]
    public void StableEntityId_OrderingIsOrdinalAndDeterministic()
    {
        StableEntityId[] entityIds =
        [
            new("REQ-010"),
            new("PLAN-STEP-001"),
            new("AC-002"),
            new("REQ-001"),
            new("AC-001")
        ];

        Array.Sort(
            entityIds);

        Assert.Equal(
            [
                "AC-001",
                "AC-002",
                "PLAN-STEP-001",
                "REQ-001",
                "REQ-010"
            ],
            entityIds
                .Select(
                    entityId =>
                        entityId.Value)
                .ToArray());
    }

    [Fact]
    public void StableEntityId_EqualityUsesCanonicalValue()
    {
        Assert.Equal(
            new StableEntityId(
                "REQ-001"),
            new StableEntityId(
                "REQ-001"));

        Assert.NotEqual(
            new StableEntityId(
                "REQ-001"),
            new StableEntityId(
                "REQ-002"));
    }
}
