using System.Globalization;
using System.Text;

namespace AiRepoKit.Spec.Projection;

public static class SpecMarkdownProjector
{
    private const string _warning =
        "> Derived projection only. Canonical state is the corresponding JSON artifact under `.ai/specs/<spec-id>/`.";

    public static string Project(
        RequirementSet requirementSet_)
    {
        ArgumentNullException.ThrowIfNull(
            requirementSet_);

        StringBuilder markdown =
            CreateHeader(
                "Requirement Set",
                requirementSet_.ArtifactIdentity,
                requirementSet_.SchemaId,
                requirementSet_.SchemaVersion,
                requirementSet_.Revision,
                null,
                SpecSemanticDigest.Compute(
                    requirementSet_));

        AppendSectionHeading(
            markdown,
            "Inputs");

        if (requirementSet_.Inputs.Count == 0)
        {
            markdown.Append("_None._\n");
        }
        else
        {
            foreach (RequirementInput input in requirementSet_.Inputs.OrderBy(
                         input_ => input_.Id.Value,
                         StringComparer.Ordinal))
            {
                AppendItemHeading(
                    markdown,
                    $"`{input.Id.Value}`");
                markdown.Append("Text: <code>");
                markdown.Append(
                    EscapeText(
                        input.Text));
                markdown.Append("</code>\n");
            }
        }

        AppendSectionHeading(
            markdown,
            "Requirements");

        if (requirementSet_.Requirements.Count == 0)
        {
            markdown.Append("_None._\n");
        }
        else
        {
            foreach (Requirement requirement in requirementSet_.Requirements.OrderBy(
                         requirement_ => requirement_.Id.Value,
                         StringComparer.Ordinal))
            {
                AppendItemHeading(
                    markdown,
                    $"`{requirement.Id.Value}`");
                markdown.Append("Statement: <code>");
                markdown.Append(
                    EscapeText(
                        requirement.Statement));
                markdown.Append("</code>\n\n");
                AppendReferences(
                    markdown,
                    "Source inputs",
                    requirement.SourceInputIds);
            }
        }

        return Complete(
            markdown);
    }

    public static string Project(
        WorkSpec workSpec_)
    {
        ArgumentNullException.ThrowIfNull(
            workSpec_);

        StringBuilder markdown =
            CreateHeader(
                "Work Spec",
                workSpec_.ArtifactIdentity,
                workSpec_.SchemaId,
                workSpec_.SchemaVersion,
                workSpec_.Revision,
                ("RequirementSet revision", workSpec_.RequirementSetRevision),
                SpecSemanticDigest.Compute(
                    workSpec_));

        AppendSectionHeading(
            markdown,
            "Constraints");
        AppendWorkItems(
            markdown,
            workSpec_.Constraints,
            constraint_ => constraint_.Id,
            constraint_ => constraint_.Statement,
            constraint_ => constraint_.RequirementIds);

        AppendSectionHeading(
            markdown,
            "Acceptance Criteria");
        AppendWorkItems(
            markdown,
            workSpec_.AcceptanceCriteria,
            criterion_ => criterion_.Id,
            criterion_ => criterion_.Statement,
            criterion_ => criterion_.RequirementIds);

        return Complete(
            markdown);
    }

    public static string Project(
        ImplementationPlan implementationPlan_)
    {
        ArgumentNullException.ThrowIfNull(
            implementationPlan_);

        StringBuilder markdown =
            CreateHeader(
                "Implementation Plan",
                implementationPlan_.ArtifactIdentity,
                implementationPlan_.SchemaId,
                implementationPlan_.SchemaVersion,
                implementationPlan_.Revision,
                ("WorkSpec revision", implementationPlan_.WorkSpecRevision),
                SpecSemanticDigest.Compute(
                    implementationPlan_));

        AppendSectionHeading(
            markdown,
            "Steps");

        if (implementationPlan_.Steps.Count == 0)
        {
            markdown.Append("_None._\n");
        }
        else
        {
            for (int index = 0; index < implementationPlan_.Steps.Count; index++)
            {
                PlanStep step =
                    implementationPlan_.Steps[index];

                AppendItemHeading(
                    markdown,
                    $"{(index + 1).ToString(CultureInfo.InvariantCulture)}. `{step.Id.Value}`");
                markdown.Append("Statement: <code>");
                markdown.Append(
                    EscapeText(
                        step.Statement));
                markdown.Append("</code>\n\n");
                AppendReferences(
                    markdown,
                    "Requirements",
                    step.RequirementIds);
                markdown.Append('\n');
                AppendReferences(
                    markdown,
                    "Acceptance criteria",
                    step.AcceptanceCriterionIds);
            }
        }

        return Complete(
            markdown);
    }

    private static StringBuilder CreateHeader(
        string title_,
        string artifactIdentity_,
        string schemaId_,
        int schemaVersion_,
        ArtifactRevision revision_,
        (string Label, ArtifactRevision Revision)? dependencyRevision_,
        string semanticDigest_)
    {
        StringBuilder markdown =
            new();

        markdown.Append("# ");
        markdown.Append(title_);
        markdown.Append("\n\n");
        markdown.Append(_warning);
        markdown.Append("\n\n");
        AppendMetadata(
            markdown,
            "Artifact identity",
            artifactIdentity_);
        AppendMetadata(
            markdown,
            "Schema ID",
            schemaId_);
        AppendMetadata(
            markdown,
            "Schema version",
            schemaVersion_.ToString(
                CultureInfo.InvariantCulture));
        AppendMetadata(
            markdown,
            "Revision",
            revision_.Value.ToString(
                CultureInfo.InvariantCulture));

        if (dependencyRevision_.HasValue)
        {
            AppendMetadata(
                markdown,
                dependencyRevision_.Value.Label,
                dependencyRevision_.Value.Revision.Value.ToString(
                    CultureInfo.InvariantCulture));
        }

        AppendMetadata(
            markdown,
            "Semantic digest",
            $"{SpecSchema.DigestAlgorithm}:{semanticDigest_}");

        return markdown;
    }

    private static void AppendMetadata(
        StringBuilder markdown_,
        string label_,
        string value_)
    {
        markdown_.Append(label_);
        markdown_.Append(": `");
        markdown_.Append(value_);
        markdown_.Append("`\n");
    }

    private static void AppendSectionHeading(
        StringBuilder markdown_,
        string heading_)
    {
        markdown_.Append("\n## ");
        markdown_.Append(heading_);
        markdown_.Append("\n\n");
    }

    private static void AppendItemHeading(
        StringBuilder markdown_,
        string heading_)
    {
        if (markdown_.Length > 0 && markdown_[markdown_.Length - 1] != '\n')
        {
            markdown_.Append('\n');
        }

        if (markdown_.Length > 1 && markdown_[markdown_.Length - 2] != '\n')
        {
            markdown_.Append('\n');
        }

        markdown_.Append("### ");
        markdown_.Append(heading_);
        markdown_.Append("\n\n");
    }

    private static void AppendWorkItems<T>(
        StringBuilder markdown_,
        IReadOnlyList<T> items_,
        Func<T, StableEntityId> idSelector_,
        Func<T, string> statementSelector_,
        Func<T, IReadOnlyList<StableEntityId>> requirementIdsSelector_)
    {
        if (items_.Count == 0)
        {
            markdown_.Append("_None._\n");
            return;
        }

        foreach (T item in items_.OrderBy(
                     item_ => idSelector_(item_).Value,
                     StringComparer.Ordinal))
        {
            AppendItemHeading(
                markdown_,
                $"`{idSelector_(item).Value}`");
            markdown_.Append("Statement: <code>");
            markdown_.Append(
                EscapeText(
                    statementSelector_(item)));
            markdown_.Append("</code>\n\n");
            AppendReferences(
                markdown_,
                "Requirements",
                requirementIdsSelector_(item));
        }
    }

    private static void AppendReferences(
        StringBuilder markdown_,
        string label_,
        IReadOnlyList<StableEntityId> references_)
    {
        markdown_.Append(label_);

        if (references_.Count == 0)
        {
            markdown_.Append(": _none_\n");
            return;
        }

        markdown_.Append(":\n\n");

        foreach (StableEntityId reference in references_.OrderBy(
                     reference_ => reference_.Value,
                     StringComparer.Ordinal))
        {
            markdown_.Append("- `");
            markdown_.Append(reference.Value);
            markdown_.Append("`\n");
        }
    }

    private static string EscapeText(
        string text_)
    {
        StringBuilder escaped =
            new(text_.Length);

        foreach (char character in text_)
        {
            switch (character)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                case '&':
                    escaped.Append("&amp;");
                    break;
                case '<':
                    escaped.Append("&lt;");
                    break;
                case '>':
                    escaped.Append("&gt;");
                    break;
                default:
                    if (character < ' ' || character == '\u007f')
                    {
                        escaped.Append("\\u");
                        escaped.Append(
                            ((int)character).ToString(
                                "X4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        escaped.Append(character);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    private static string Complete(
        StringBuilder markdown_)
    {
        while (markdown_.Length > 0 && markdown_[markdown_.Length - 1] == '\n')
        {
            markdown_.Length--;
        }

        markdown_.Append('\n');
        return markdown_.ToString();
    }
}
