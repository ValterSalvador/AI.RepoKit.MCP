using System.Text.Json;
using AiRepoKit.Spec.Context;
using AiRepoKit.Spec.Verification;

namespace AiRepoKit.Cli.Services.SpecVerification;

/// <summary>
/// Adapter for <c>secret-scan</c>: reads .ai/generated/reports/secret-scan-report.json.
/// Supporting when redactedOnly=true AND findingCount==0.
/// Contradicting when redactedOnly=true AND findingCount&gt;0.
/// Inconclusive when unavailable/stale/unparseable/non-redacted.
/// NEVER surfaces secret values or raw sensitive matches.
/// </summary>
public sealed class SecretScanEvidenceAdapter : ISpecVerificationEvidenceAdapter
{
    public string RepositoryEvidenceId => "secret-scan";

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

            // Safety rule: must be redactedOnly
            bool? redactedOnly = TryGetBool(root, "redactedOnly", "RedactedOnly");
            if (redactedOnly is not true)
            {
                return SpecVerificationEvidenceDisposition.Inconclusive;
            }

            int findingCount = GetSafeFindingCount(root);
            return findingCount == 0
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
                "Secret-scan report: redactedOnly=true, finding count=0.",
            SpecVerificationEvidenceDisposition.Contradicting =>
                "Secret-scan report: redactedOnly=true, one or more findings detected.",
            _ =>
                evidence_.Availability != RepositoryEvidenceAvailability.Available
                    ? "Secret-scan report is not available."
                    : evidence_.Freshness == RepositoryEvidenceFreshness.Stale
                        ? "Secret-scan report is stale."
                        : "Secret-scan report is not acceptable: not redacted-only, missing, or unparseable."
        };
    }

    private static bool? TryGetBool(JsonElement root_, params string[] names_)
    {
        foreach (string name in names_)
        {
            if (root_.TryGetProperty(name, out JsonElement el))
            {
                if (el.ValueKind == JsonValueKind.True) return true;
                if (el.ValueKind == JsonValueKind.False) return false;
            }
        }

        return null;
    }

    private static int GetSafeFindingCount(JsonElement root_)
    {
        foreach (string name in new[] { "findingCount", "FindingCount" })
        {
            if (root_.TryGetProperty(name, out JsonElement el) &&
                el.ValueKind == JsonValueKind.Number &&
                el.TryGetInt32(out int value))
            {
                return Math.Max(0, value);
            }
        }

        foreach (string name in new[] { "findings", "Findings" })
        {
            if (root_.TryGetProperty(name, out JsonElement el) &&
                el.ValueKind == JsonValueKind.Array)
            {
                return el.GetArrayLength();
            }
        }

        return 0;
    }
}
