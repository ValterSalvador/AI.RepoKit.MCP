using System.Text;
using System.Text.Json;

namespace AiRepoKit.Spec;

public static class SpecSemanticCanonicalizer
{
    public static string Canonicalize(
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        EnsureSupportedSchema(
            requirementSet_.SchemaId,
            requirementSet_.SchemaVersion);

        return WriteCanonical(
            writer_ =>
            {
                WriteHeader(
                    writer_,
                    "requirementSet",
                    requirementSet_.ArtifactIdentity,
                    requirementSet_.SchemaId,
                    requirementSet_.SchemaVersion);

                writer_.WritePropertyName(
                    "inputs");

                writer_.WriteStartArray();

                foreach (RequirementInput input in
                         requirementSet_.Inputs.OrderBy(
                             input_ =>
                                 input_.Id.Value,
                             StringComparer.Ordinal))
                {
                    writer_.WriteStartObject();

                    writer_.WriteString(
                        "id",
                        input.Id.Value);

                    writer_.WriteString(
                        "text",
                        input.Text);

                    writer_.WriteEndObject();
                }

                writer_.WriteEndArray();

                writer_.WritePropertyName(
                    "requirements");

                writer_.WriteStartArray();

                foreach (Requirement requirement in
                         requirementSet_.Requirements.OrderBy(
                             requirement_ =>
                                 requirement_.Id.Value,
                             StringComparer.Ordinal))
                {
                    writer_.WriteStartObject();

                    writer_.WriteString(
                        "id",
                        requirement.Id.Value);

                    writer_.WriteString(
                        "statement",
                        requirement.Statement);

                    WriteSortedIds(
                        writer_,
                        "sourceInputIds",
                        requirement.SourceInputIds);

                    writer_.WriteEndObject();
                }

                writer_.WriteEndArray();
            });
    }

    public static string Canonicalize(
        WorkSpec workSpec_)
    {
        ArgumentNullException.ThrowIfNull(
            workSpec_);

        EnsureSupportedSchema(
            workSpec_.SchemaId,
            workSpec_.SchemaVersion);

        return WriteCanonical(
            writer_ =>
            {
                WriteHeader(
                    writer_,
                    "workSpec",
                    workSpec_.ArtifactIdentity,
                    workSpec_.SchemaId,
                    workSpec_.SchemaVersion);

                writer_.WriteNumber(
                    "requirementSetRevision",
                    workSpec_.RequirementSetRevision.Value);

                writer_.WritePropertyName(
                    "constraints");

                writer_.WriteStartArray();

                foreach (Constraint constraint in
                         workSpec_.Constraints.OrderBy(
                             constraint_ =>
                                 constraint_.Id.Value,
                             StringComparer.Ordinal))
                {
                    writer_.WriteStartObject();

                    writer_.WriteString(
                        "id",
                        constraint.Id.Value);

                    writer_.WriteString(
                        "statement",
                        constraint.Statement);

                    WriteSortedIds(
                        writer_,
                        "requirementIds",
                        constraint.RequirementIds);

                    writer_.WriteEndObject();
                }

                writer_.WriteEndArray();

                writer_.WritePropertyName(
                    "acceptanceCriteria");

                writer_.WriteStartArray();

                foreach (AcceptanceCriterion criterion in
                         workSpec_.AcceptanceCriteria.OrderBy(
                             criterion_ =>
                                 criterion_.Id.Value,
                             StringComparer.Ordinal))
                {
                    writer_.WriteStartObject();

                    writer_.WriteString(
                        "id",
                        criterion.Id.Value);

                    writer_.WriteString(
                        "statement",
                        criterion.Statement);

                    WriteSortedIds(
                        writer_,
                        "requirementIds",
                        criterion.RequirementIds);

                    writer_.WriteEndObject();
                }

                writer_.WriteEndArray();
            });
    }

    public static string Canonicalize(
        ImplementationPlan implementationPlan_)
    {
        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        EnsureSupportedSchema(
            implementationPlan_.SchemaId,
            implementationPlan_.SchemaVersion);

        return WriteCanonical(
            writer_ =>
            {
                WriteHeader(
                    writer_,
                    "implementationPlan",
                    implementationPlan_.ArtifactIdentity,
                    implementationPlan_.SchemaId,
                    implementationPlan_.SchemaVersion);

                writer_.WriteNumber(
                    "workSpecRevision",
                    implementationPlan_.WorkSpecRevision.Value);

                writer_.WritePropertyName(
                    "steps");

                writer_.WriteStartArray();

                foreach (PlanStep step in
                         implementationPlan_.Steps)
                {
                    writer_.WriteStartObject();

                    writer_.WriteString(
                        "id",
                        step.Id.Value);

                    writer_.WriteString(
                        "statement",
                        step.Statement);

                    WriteSortedIds(
                        writer_,
                        "requirementIds",
                        step.RequirementIds);

                    WriteSortedIds(
                        writer_,
                        "acceptanceCriterionIds",
                        step.AcceptanceCriterionIds);

                    writer_.WriteEndObject();
                }

                writer_.WriteEndArray();
            });
    }

    private static string WriteCanonical(
        Action<Utf8JsonWriter> writeContent_)
    {
        using MemoryStream stream =
            new();

        using Utf8JsonWriter writer =
            new(
                stream,
                new JsonWriterOptions
                {
                    Indented =
                        false,
                    SkipValidation =
                        false
                });

        writer.WriteStartObject();

        writeContent_(
            writer);

        writer.WriteEndObject();

        writer.Flush();

        return Encoding.UTF8.GetString(
            stream.ToArray());
    }

    private static void WriteHeader(
        Utf8JsonWriter writer_,
        string artifactKind_,
        string artifactIdentity_,
        string schemaId_,
        int schemaVersion_)
    {
        writer_.WriteString(
            "canonicalizationId",
            SpecSchema.CanonicalizationId);

        writer_.WriteNumber(
            "canonicalizationVersion",
            SpecSchema.CanonicalizationVersion);

        writer_.WriteString(
            "artifactKind",
            artifactKind_);

        writer_.WriteString(
            "artifactIdentity",
            artifactIdentity_);

        writer_.WriteString(
            "schemaId",
            schemaId_);

        writer_.WriteNumber(
            "schemaVersion",
            schemaVersion_);
    }

    private static void WriteSortedIds(
        Utf8JsonWriter writer_,
        string propertyName_,
        IReadOnlyList<StableEntityId> entityIds_)
    {
        writer_.WritePropertyName(
            propertyName_);

        writer_.WriteStartArray();

        foreach (StableEntityId entityId in
                 entityIds_.OrderBy(
                     entityId_ =>
                         entityId_.Value,
                     StringComparer.Ordinal))
        {
            writer_.WriteStringValue(
                entityId.Value);
        }

        writer_.WriteEndArray();
    }

    private static void EnsureSupportedSchema(
        string schemaId_,
        int schemaVersion_)
    {
        if (!string.Equals(
                schemaId_,
                SpecSchema.SchemaId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported schema ID '{schemaId_}'.",
                nameof(schemaId_));
        }

        if (schemaVersion_ !=
            SpecSchema.SchemaVersion)
        {
            throw new ArgumentException(
                $"Unsupported schema version '{schemaVersion_}'.",
                nameof(schemaVersion_));
        }
    }
}
