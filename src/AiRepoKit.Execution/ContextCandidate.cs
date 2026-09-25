namespace AiRepoKit.Execution;

public sealed record ContextCandidate
{
    public string Id
    {
        get;
    }

    public string Content
    {
        get;
    }

    public int RelevanceScore
    {
        get;
    }

    public ContextCandidate(
        string id_,
        string content_,
        int relevanceScore_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            id_,
            nameof(id_));

        ArgumentNullException.ThrowIfNull(
            content_,
            nameof(content_));

        if (content_.Length == 0)
        {
            throw new ArgumentException(
                "Content must not be empty.",
                nameof(content_));
        }

        if (relevanceScore_ < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relevanceScore_),
                relevanceScore_,
                "Relevance score must not be negative.");
        }

        this.Id =
            id_;
        this.Content =
            content_;
        this.RelevanceScore =
            relevanceScore_;
    }
}
