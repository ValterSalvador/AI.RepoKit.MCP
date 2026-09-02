using System.Text.Json;
using AiRepoKit.Spec;
using Xunit;

namespace AiRepoKit.Cli.Tests.Spec;

public sealed class SpecSchemaFixtureTests
{
    private static readonly string _fixtureRoot =
        Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Spec",
            "V1");

    [Fact]
    public void Schema_IsExplicitlyVersionedJsonSchema202012()
    {
        using JsonDocument document =
            LoadSchema();

        JsonElement root =
            document.RootElement;

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            root.GetProperty(
                "$schema").GetString());

        Assert.Equal(
            "https://ai.repokit.dev/schemas/spec/v1/spec-ir.schema.json",
            root.GetProperty(
                "$id").GetString());

        Assert.Equal(
            "AI.RepoKit Spec IR v1",
            root.GetProperty(
                "title").GetString());
    }

    [Fact]
    public void Schema_CoversAllCanonicalIrContracts()
    {
        using JsonDocument document =
            LoadSchema();

        JsonElement definitions =
            document
                .RootElement
                .GetProperty(
                    "$defs");

        string[] requiredDefinitions =
        [
            "requirementInput",
            "requirement",
            "requirementSet",
            "constraint",
            "acceptanceCriterion",
            "workSpec",
            "planStep",
            "implementationPlan",
            "approval",
            "verificationEvidence",
            "verificationResult"
        ];

        foreach (string definition in
                 requiredDefinitions)
        {
            Assert.True(
                definitions.TryGetProperty(
                    definition,
                    out _),
                $"Missing schema definition '{definition}'.");
        }
    }

    [Fact]
    public void Schema_ObjectContractsRejectUnknownMembers()
    {
        using JsonDocument document =
            LoadSchema();

        JsonElement definitions =
            document
                .RootElement
                .GetProperty(
                    "$defs");

        foreach (JsonProperty definition in
                 definitions.EnumerateObject())
        {
            JsonElement value =
                definition.Value;

            if (!value.TryGetProperty(
                    "type",
                    out JsonElement type) ||
                type.GetString() != "object")
            {
                continue;
            }

            Assert.True(
                value.TryGetProperty(
                    "additionalProperties",
                    out JsonElement additionalProperties),
                $"Definition '{definition.Name}' must explicitly declare additionalProperties.");

            Assert.False(
                additionalProperties.GetBoolean());
        }
    }

    [Fact]
    public void Schema_FreezesStableIdAndRevisionRules()
    {
        using JsonDocument document =
            LoadSchema();

        JsonElement definitions =
            document
                .RootElement
                .GetProperty(
                    "$defs");

        string requirementPattern =
            definitions
                .GetProperty(
                    "requirement")
                .GetProperty(
                    "properties")
                .GetProperty(
                    "id")
                .GetProperty(
                    "pattern")
                .GetString()!;

        Assert.Equal(
            "^REQ-[0-9]{3,}$",
            requirementPattern);

        int revisionMinimum =
            definitions
                .GetProperty(
                    "requirementSet")
                .GetProperty(
                    "properties")
                .GetProperty(
                    "revision")
                .GetProperty(
                    "minimum")
                .GetInt32();

        Assert.Equal(
            1,
            revisionMinimum);
    }

    [Fact]
    public void ValidRequirementSetFixture_RoundTripsDeterministically()
    {
        AssertStableRoundTrip<RequirementSet>(
            "valid",
            "requirements.json");
    }

    [Fact]
    public void ValidWorkSpecFixture_RoundTripsDeterministically()
    {
        AssertStableRoundTrip<WorkSpec>(
            "valid",
            "work-spec.json");
    }

    [Fact]
    public void ValidImplementationPlanFixture_RoundTripsDeterministically()
    {
        AssertStableRoundTrip<ImplementationPlan>(
            "valid",
            "implementation-plan.json");
    }

    [Fact]
    public void ValidApprovalFixture_RoundTripsDeterministically()
    {
        AssertStableRoundTrip<Approval>(
            "valid",
            "approval.json");
    }

    [Fact]
    public void ValidVerificationEvidenceFixture_RoundTripsDeterministically()
    {
        AssertStableRoundTrip<VerificationEvidence>(
            "valid",
            "verification-evidence.json");
    }

    [Fact]
    public void ValidVerificationResultFixture_RoundTripsDeterministically()
    {
        AssertStableRoundTrip<VerificationResult>(
            "valid",
            "verification-result.json");
    }

    [Fact]
    public void ValidFixtureSet_PassesTraceabilityValidation()
    {
        RequirementSet requirementSet =
            Load<RequirementSet>(
                "valid",
                "requirements.json");

        WorkSpec workSpec =
            Load<WorkSpec>(
                "valid",
                "work-spec.json");

        ImplementationPlan plan =
            Load<ImplementationPlan>(
                "valid",
                "implementation-plan.json");

        Approval approval =
            Load<Approval>(
                "valid",
                "approval.json");

        VerificationEvidence evidence =
            Load<VerificationEvidence>(
                "valid",
                "verification-evidence.json");

        VerificationResult result =
            Load<VerificationResult>(
                "valid",
                "verification-result.json");

        Assert.Empty(
            RequirementSetValidator.Validate(
                requirementSet));

        Assert.Empty(
            WorkSpecValidator.Validate(
                workSpec,
                requirementSet));

        Assert.Empty(
            ImplementationPlanValidator.Validate(
                plan,
                workSpec,
                requirementSet));

        Assert.Empty(
            ApprovalValidator.Validate(
                approval));

        Assert.Empty(
            VerificationValidator.Validate(
                [evidence],
                [result],
                workSpec,
                plan));
    }

    [Fact]
    public void UnsupportedSchemaVersionFixture_IsRejectedByContractValidator()
    {
        RequirementSet requirementSet =
            Load<RequirementSet>(
                "invalid",
                "unsupported-version.requirements.json");

        SpecValidationError error =
            Assert.Single(
                RequirementSetValidator.Validate(
                    requirementSet));

        Assert.Equal(
            SpecValidationErrorCodes.UnsupportedSchemaVersion,
            error.Code);
    }

    [Fact]
    public void DanglingReferenceFixture_IsRejectedDeterministically()
    {
        RequirementSet requirementSet =
            Load<RequirementSet>(
                "valid",
                "requirements.json");

        WorkSpec workSpec =
            Load<WorkSpec>(
                "invalid",
                "dangling-reference.work-spec.json");

        SpecValidationError error =
            Assert.Single(
                WorkSpecValidator.Validate(
                    workSpec,
                    requirementSet));

        Assert.Equal(
            SpecValidationErrorCodes.DanglingReference,
            error.Code);

        Assert.Equal(
            "AC-001",
            error.SourceEntityId);

        Assert.Equal(
            "REQ-999",
            error.TargetEntityId);
    }

    [Fact]
    public void UnknownMemberFixture_IsRejectedDuringDeserialization()
    {
        string json =
            ReadFixture(
                "invalid",
                "unknown-member.requirement-input.json");

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<RequirementInput>(
                    json));
    }

    [Fact]
    public void InvalidStableIdFixture_IsRejectedDuringDeserialization()
    {
        string json =
            ReadFixture(
                "invalid",
                "invalid-id.requirement-input.json");

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<RequirementInput>(
                    json));
    }

    [Fact]
    public void IntegerEnumFixture_IsRejectedDuringDeserialization()
    {
        string json =
            ReadFixture(
                "invalid",
                "integer-enum.verification-result.json");

        Assert.Throws<JsonException>(
            () =>
                SpecJsonSerializer.Deserialize<VerificationResult>(
                    json));
    }

    private static JsonDocument LoadSchema()
    {
        return JsonDocument.Parse(
            ReadFixture(
                "schema",
                "spec-ir.schema.json"));
    }

    private static T Load<T>(
        string category_,
        string fileName_)
    {
        return SpecJsonSerializer.Deserialize<T>(
            ReadFixture(
                category_,
                fileName_));
    }

    private static void AssertStableRoundTrip<T>(
        string category_,
        string fileName_)
    {
        T original =
            Load<T>(
                category_,
                fileName_);

        string first =
            SpecJsonSerializer.Serialize(
                original);

        T restored =
            SpecJsonSerializer.Deserialize<T>(
                first);

        string second =
            SpecJsonSerializer.Serialize(
                restored);

        Assert.Equal(
            first,
            second);
    }

    private static string ReadFixture(
        string category_,
        string fileName_)
    {
        string path =
            Path.Combine(
                _fixtureRoot,
                category_,
                fileName_);

        Assert.True(
            File.Exists(
                path),
            $"Fixture not found: {path}");

        return File.ReadAllText(
            path);
    }
}
