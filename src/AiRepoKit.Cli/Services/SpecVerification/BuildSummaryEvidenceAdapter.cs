using System.Text.Json;
using System.Text.Json.Nodes;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Verification;

namespace AiRepoKit.Cli.Services.SpecVerification;

/// <summary>
/// Adapter for <c>build-summary</c>: reads .ai/generated/reports/latest-build-summary.json.
/// Supporting when restoreExitCode==0 AND buildExitCode==0; Contradicting otherwise;
/// Inconclusive when unavailable/stale/unparseable.
/// Does NOT run a build; reads the existing structured report only.
/// </summary>
public sealed class BuildSummaryEvidenceAdapter : ISpecVerificationEvidenceAdapter
{
    public string RepositoryEvidenceId => "build-summary";

    public SpecVerificationEvidenceDisposition Evaluate(
        RepositoryEvidence evidence_,
        string repoRoot_)
    {
        if (evidence_.Availability != RepositoryEvidenceAvailability.Available)
        {
            return SpecVerificationEvidenceDisposition.Inconclusive;
        }

        if (evidence_.Freshness == RepositoryEvidenceFreshness.Stale)
        {
            return SpecVerificationEvidenceDisposition.Inconclusive;
        }

        string fullPath = Path.Combine(repoRoot_, evidence_.Reference.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
        {
            return SpecVerificationEvidenceDisposition.Inconclusive;
        }

        try
        {
            string json = File.ReadAllText(fullPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            int? restoreExitCode = TryGetInt(root, "restoreExitCode", "RestoreExitCode");
            int? buildExitCode = TryGetInt(root, "buildExitCode", "BuildExitCode");

            if (restoreExitCode is null || buildExitCode is null)
            {
                return SpecVerificationEvidenceDisposition.Inconclusive;
            }

            return restoreExitCode.Value == 0 && buildExitCode.Value == 0
                ? SpecVerificationEvidenceDisposition.Supporting
                : SpecVerificationEvidenceDisposition.Contradicting;
        }
        catch
        {
            return SpecVerificationEvidenceDisposition.Inconclusive;
        }
    }

    public string GetReason(
        RepositoryEvidence evidence_,
        SpecVerificationEvidenceDisposition disposition_)
    {
        return disposition_ switch
        {
            SpecVerificationEvidenceDisposition.Supporting =>
                "Build report: restoreExitCode=0 and buildExitCode=0.",
            SpecVerificationEvidenceDisposition.Contradicting =>
                "Build report: non-zero exit code in restore or build step.",
            _ =>
                evidence_.Availability != RepositoryEvidenceAvailability.Available
                    ? "Build summary report is not available."
                    : evidence_.Freshness == RepositoryEvidenceFreshness.Stale
                        ? "Build summary report is stale."
                        : "Build summary report could not be parsed or fields are missing."
        };
    }

    private static int? TryGetInt(JsonElement root_, params string[] names_)
    {
        foreach (string name in names_)
        {
            if (root_.TryGetProperty(name, out JsonElement el) &&
                el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out int value))
            {
                return value;
            }
        }

        return null;
    }
}
