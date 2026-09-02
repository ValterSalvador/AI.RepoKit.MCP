using System.Text.RegularExpressions;

namespace AiRepoKit.Spec;

public readonly record struct StableEntityId :
    IComparable<StableEntityId>
{
    private static readonly Regex _pattern =
        new(
            "^(?:[A-Z]+-)+[0-9]{3,}$",
            RegexOptions.CultureInvariant |
            RegexOptions.NonBacktracking);

    private readonly string? _value;

    public string Value =>
        this._value ??
        string.Empty;

    public StableEntityId(
        string value_)
    {
        if (!IsValid(
                value_))
        {
            throw new ArgumentException(
                "Stable entity ID must use uppercase ASCII segments and end with a numeric suffix of at least three digits.",
                nameof(value_));
        }

        this._value =
            value_;
    }

    public int CompareTo(
        StableEntityId other_)
    {
        return string.Compare(
            this.Value,
            other_.Value,
            StringComparison.Ordinal);
    }

    public override string ToString()
    {
        return this.Value;
    }

    public static bool IsValid(
        string? value_)
    {
        return
            !string.IsNullOrEmpty(
                value_) &&
            _pattern.IsMatch(
                value_);
    }

    public static bool TryParse(
        string? value_,
        out StableEntityId entityId_)
    {
        if (!IsValid(
                value_))
        {
            entityId_ =
                default;

            return false;
        }

        entityId_ =
            new StableEntityId(
                value_!);

        return true;
    }
}
