using System.Globalization;
using System.Text;
using AiRepoKit.Spec.Lifecycle;
using AiRepoKit.Spec.Persistence;

namespace AiRepoKit.Spec.Projection;

public static class ImplementationChecklistProjector
{
    private const string _missingPlanMessage =
        "Cannot project an implementation checklist because no canonical ImplementationPlan exists.";

    private const string _warning =
        "> Derived projection only. No checklist state is persisted.";

    public static ImplementationChecklistProjection Project(
        SpecId specId_,
        SpecWorkspaceSnapshot snapshot_,
        SpecApprovalLedger? ledger_)
    {
        ArgumentNullException.ThrowIfNull(
            snapshot_);

        ImplementationPlan implementationPlan =
            snapshot_.ImplementationPlan ??
            throw new InvalidOperationException(
                _missingPlanMessage);

        SpecArtifactApprovalStatus approvalStatus =
            SpecApprovalStatusEvaluator.Evaluate(
                SpecArtifactKind.ImplementationPlan,
                snapshot_,
                ledger_)!;

        ImplementationChecklistStepProjection[] steps =
            implementationPlan
                .Steps
                .Select(
                    step_ =>
                        new ImplementationChecklistStepProjection
                        {
                            Id =
                                step_.Id,
                            Statement =
                                step_.Statement,
                            RequirementIds =
                                step_
                                    .RequirementIds
                                    .OrderBy(
                                        id_ =>
                                            id_.Value,
                                        StringComparer.Ordinal)
                                    .ToArray(),
                            AcceptanceCriterionIds =
                                step_
                                    .AcceptanceCriterionIds
                                    .OrderBy(
                                        id_ =>
                                            id_.Value,
                                        StringComparer.Ordinal)
                                    .ToArray()
                        })
                .ToArray();

        return new ImplementationChecklistProjection
        {
            SpecId =
                specId_.Value,
            PlanRevision =
                implementationPlan.Revision,
            WorkSpecRevision =
                implementationPlan.WorkSpecRevision,
            SemanticDigest =
                SpecSemanticDigest.Compute(
                    implementationPlan),
            Stale =
                snapshot_.IsImplementationPlanStale,
            ApprovalStatus =
                approvalStatus.Status,
            Steps =
                steps
        };
    }

    public static string ProjectJson(
        ImplementationChecklistProjection projection_)
    {
        ArgumentNullException.ThrowIfNull(
            projection_);

        return SpecJsonSerializer.Serialize(
            projection_);
    }

    public static string ProjectMarkdown(
        ImplementationChecklistProjection projection_)
    {
        ArgumentNullException.ThrowIfNull(
            projection_);

        StringBuilder markdown =
            new();

        markdown.Append(
            "# Implementation Checklist: ");
        markdown.Append(
            projection_.SpecId);
        markdown.Append(
            "\n\n");
        markdown.Append(
            _warning);
        markdown.Append(
            "\n\n");

        markdown.Append(
            "Plan Revision: ");
        markdown.Append(
            projection_
                .PlanRevision
                .Value
                .ToString(
                    CultureInfo.InvariantCulture));
        markdown.Append(
            '\n');

        markdown.Append(
            "WorkSpec Revision: ");
        markdown.Append(
            projection_
                .WorkSpecRevision
                .Value
                .ToString(
                    CultureInfo.InvariantCulture));
        markdown.Append(
            '\n');

        markdown.Append(
            "Semantic Digest: sha256:");
        markdown.Append(
            projection_.SemanticDigest);
        markdown.Append(
            '\n');

        markdown.Append(
            "Status: ");
        markdown.Append(
            projection_.Stale
                ? "STALE"
                : "CURRENT");
        markdown.Append(
            '\n');

        markdown.Append(
            "Approval Status: ");
        markdown.Append(
            FormatApprovalStatus(
                projection_.ApprovalStatus));
        markdown.Append(
            "\n\n");

        markdown.Append(
            "## Steps\n\n");

        if (projection_.Steps.Count == 0)
        {
            markdown.Append(
                "_None._\n");

            return Complete(
                markdown);
        }

        foreach (ImplementationChecklistStepProjection step in
                 projection_.Steps)
        {
            markdown.Append(
                "- [ ] `");
            markdown.Append(
                step.Id.Value);
            markdown.Append(
                "` — <code>");
            markdown.Append(
                EscapeText(
                    step.Statement));
            markdown.Append(
                "</code>\n");

            markdown.Append(
                "  - Requirements: ");
            AppendReferences(
                markdown,
                step.RequirementIds);
            markdown.Append(
                '\n');

            markdown.Append(
                "  - Acceptance Criteria: ");
            AppendReferences(
                markdown,
                step.AcceptanceCriterionIds);
            markdown.Append(
                '\n');
        }

        return Complete(
            markdown);
    }

    private static void AppendReferences(
        StringBuilder markdown_,
        IReadOnlyList<StableEntityId> references_)
    {
        if (references_.Count == 0)
        {
            markdown_.Append(
                "_none_");

            return;
        }

        for (int index = 0; index < references_.Count; index++)
        {
            if (index > 0)
            {
                markdown_.Append(
                    ", ");
            }

            markdown_.Append(
                '`');
            markdown_.Append(
                references_[index].Value);
            markdown_.Append(
                '`');
        }
    }

    private static string FormatApprovalStatus(
        SpecApprovalStatus status_)
    {
        return status_ switch
        {
            SpecApprovalStatus.NotApproved =>
                "NOT_APPROVED",
            SpecApprovalStatus.Current =>
                "CURRENT",
            SpecApprovalStatus.Stale =>
                "STALE",
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(status_),
                    status_,
                    "Unsupported spec approval status.")
        };
    }

    private static string EscapeText(
        string text_)
    {
        StringBuilder escaped =
            new(
                text_.Length);

        foreach (char character in text_)
        {
            switch (character)
            {
                case '\\':
                    escaped.Append(
                        "\\\\");
                    break;

                case '\r':
                    escaped.Append(
                        "\\r");
                    break;

                case '\n':
                    escaped.Append(
                        "\\n");
                    break;

                case '\t':
                    escaped.Append(
                        "\\t");
                    break;

                case '&':
                    escaped.Append(
                        "&amp;");
                    break;

                case '<':
                    escaped.Append(
                        "&lt;");
                    break;

                case '>':
                    escaped.Append(
                        "&gt;");
                    break;

                default:
                    if (character < ' ' ||
                        character == '\u007f')
                    {
                        escaped.Append(
                            "\\u");

                        escaped.Append(
                            ((int)character).ToString(
                                "X4",
                                CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        escaped.Append(
                            character);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    private static string Complete(
        StringBuilder markdown_)
    {
        while (
            markdown_.Length > 0 &&
            markdown_[markdown_.Length - 1] == '\n')
        {
            markdown_.Length--;
        }

        markdown_.Append(
            '\n');

        return markdown_.ToString();
    }
}