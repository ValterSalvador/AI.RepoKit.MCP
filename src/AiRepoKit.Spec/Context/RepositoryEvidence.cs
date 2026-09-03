namespace AiRepoKit.Spec.Context;

public sealed record RepositoryEvidence
{
    public required string EvidenceId
    {
        get;
        init;
    }

    public required string Source
    {
        get;
        init;
    }

    public required string Kind
    {
        get;
        init;
    }

    public required string Reference
    {
        get;
        init;
    }

    public RepositoryEvidenceAvailability Availability
    {
        get;
        init;
    }

    public RepositoryEvidenceFreshness Freshness
    {
        get;
        init;
    }

    public string SourceGeneratedAt
    {
        get;
        init;
    } = string.Empty;

    public string Detail
    {
        get;
        init;
    } = string.Empty;
}
