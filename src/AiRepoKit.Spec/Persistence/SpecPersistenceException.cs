namespace AiRepoKit.Spec.Persistence;

public sealed class SpecPersistenceException :
    Exception
{
    public const string ArtifactTooLarge =
        "artifact-too-large";

    public const string InvalidUtf8 =
        "invalid-utf8";

    public const string InvalidJson =
        "invalid-json";

    public const string ValidationFailed =
        "validation-failed";

    public const string MissingDependency =
        "missing-dependency";

    public const string ReadFailed =
        "read-failed";

    public const string RevisionConflict =
        "revision-conflict";

    public const string StaleDependency =
        "stale-dependency";

    public const string WriteFailed =
        "write-failed";

    public SpecPersistenceException(
        string errorCode_,
        string message_,
        SpecArtifactKind? artifactKind_ = null,
        IReadOnlyList<SpecValidationError>? validationErrors_ = null,
        Exception? innerException_ = null)
        : base(
            message_,
            innerException_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            errorCode_);

        this.ErrorCode =
            errorCode_;
        this.ArtifactKind =
            artifactKind_;
        this.ValidationErrors =
            validationErrors_?.ToArray() ??
            [];
    }

    public string ErrorCode { get; }

    public SpecArtifactKind? ArtifactKind { get; }

    public IReadOnlyList<SpecValidationError> ValidationErrors { get; }
}
