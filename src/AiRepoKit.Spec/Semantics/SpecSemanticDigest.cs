using System.Security.Cryptography;
using System.Text;

namespace AiRepoKit.Spec;

public static class SpecSemanticDigest
{
    public static string Compute(
        RequirementSet requirementSet_)
    {
        return ComputeFromCanonicalRepresentation(
            SpecSemanticCanonicalizer.Canonicalize(
                requirementSet_));
    }

    public static string Compute(
        WorkSpec workSpec_)
    {
        return ComputeFromCanonicalRepresentation(
            SpecSemanticCanonicalizer.Canonicalize(
                workSpec_));
    }

    public static string Compute(
        ImplementationPlan implementationPlan_)
    {
        return ComputeFromCanonicalRepresentation(
            SpecSemanticCanonicalizer.Canonicalize(
                implementationPlan_));
    }

    public static string ComputeFromCanonicalRepresentation(
        string canonicalRepresentation_)
    {
        ArgumentNullException.ThrowIfNull(
            canonicalRepresentation_);

        byte[] canonicalBytes =
            Encoding.UTF8.GetBytes(
                canonicalRepresentation_);

        byte[] digest =
            SHA256.HashData(
                canonicalBytes);

        return Convert
            .ToHexString(
                digest)
            .ToLowerInvariant();
    }
}
