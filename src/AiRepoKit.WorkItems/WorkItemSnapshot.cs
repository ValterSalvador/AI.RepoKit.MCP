namespace AiRepoKit.WorkItems;

public sealed class WorkItemSnapshot
{
    public WorkItemSnapshot(
        WorkItemReference reference_,
        string title_,
        WorkItemState state_,
        string nativeState_,
        Uri providerUri_)
    {
        ArgumentNullException.ThrowIfNull(
            reference_);

        ArgumentNullException.ThrowIfNull(
            title_);

        ArgumentNullException.ThrowIfNull(
            nativeState_);

        ArgumentNullException.ThrowIfNull(
            providerUri_);

        if (string.IsNullOrWhiteSpace(title_))
        {
            throw new ArgumentException(
                "Title must not be empty or whitespace-only.",
                nameof(title_));
        }

        if (string.IsNullOrWhiteSpace(nativeState_))
        {
            throw new ArgumentException(
                "Native state must not be empty or whitespace-only.",
                nameof(nativeState_));
        }

        if (!providerUri_.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "Provider URI must be absolute.",
                nameof(providerUri_));
        }

        if (!string.Equals(
                providerUri_.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                providerUri_.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Provider URI must use HTTP or HTTPS.",
                nameof(providerUri_));
        }

        Reference = reference_;
        Title = title_;
        State = state_;
        NativeState = nativeState_;
        ProviderUri = providerUri_;
    }

    public WorkItemReference Reference { get; }

    public string Title { get; }

    public WorkItemState State { get; }

    public string NativeState { get; }

    public Uri ProviderUri { get; }
}
