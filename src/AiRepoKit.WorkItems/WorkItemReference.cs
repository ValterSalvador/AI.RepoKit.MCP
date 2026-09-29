namespace AiRepoKit.WorkItems;

public sealed class WorkItemReference
{
    public WorkItemReference(
        string providerId_,
        string namespace_,
        string id_)
    {
        ProviderId =
            ValidateRequiredExactValue(
                providerId_,
                nameof(providerId_));

        Namespace =
            ValidateRequiredExactValue(
                namespace_,
                nameof(namespace_));

        Id =
            ValidateRequiredExactValue(
                id_,
                nameof(id_));
    }

    public string ProviderId { get; }

    public string Namespace { get; }

    public string Id { get; }

    private static string ValidateRequiredExactValue(
        string value_,
        string parameterName_)
    {
        ArgumentNullException.ThrowIfNull(
            value_,
            parameterName_);

        if (string.IsNullOrWhiteSpace(value_))
        {
            throw new ArgumentException(
                "Value must not be empty or whitespace-only.",
                parameterName_);
        }

        if (!string.Equals(
                value_,
                value_.Trim(),
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Outer whitespace is not allowed.",
                parameterName_);
        }

        return value_;
    }
}
