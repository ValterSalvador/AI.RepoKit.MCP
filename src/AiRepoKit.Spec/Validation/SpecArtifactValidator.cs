namespace AiRepoKit.Spec;

internal static class SpecArtifactValidator
{
    public static void ValidateIdentity(
        string artifactIdentity_,
        string expectedIdentity_,
        string artifactLabel_,
        List<SpecValidationError> errors_)
    {
        if (string.Equals(
                artifactIdentity_,
                expectedIdentity_,
                StringComparison.Ordinal))
        {
            return;
        }

        errors_.Add(
            new SpecValidationError
            {
                Code =
                    SpecValidationErrorCodes.ArtifactIdentityMismatch,
                TargetEntityId =
                    artifactIdentity_,
                Message =
                    $"{artifactLabel_} artifact identity '{artifactIdentity_}' must be '{expectedIdentity_}'."
            });
    }
}
