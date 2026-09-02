namespace AiRepoKit.Spec.Persistence;

public readonly record struct SpecId :
    IComparable<SpecId>
{
    private static readonly string[] _reservedNames =
    [
        "con",
        "prn",
        "aux",
        "nul",
        "com1",
        "com2",
        "com3",
        "com4",
        "com5",
        "com6",
        "com7",
        "com8",
        "com9",
        "lpt1",
        "lpt2",
        "lpt3",
        "lpt4",
        "lpt5",
        "lpt6",
        "lpt7",
        "lpt8",
        "lpt9"
    ];

    private readonly string? _value;

    public string Value =>
        this._value ??
        string.Empty;

    public SpecId(
        string value_)
    {
        if (!IsValid(
                value_))
        {
            throw new ArgumentException(
                "Spec ID must be 1 through 64 lowercase ASCII letters, digits, or internal hyphens and must not be a Windows device name.",
                nameof(value_));
        }

        this._value =
            value_;
    }

    public int CompareTo(
        SpecId other_)
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
        if (string.IsNullOrEmpty(
                value_) ||
            value_.Length > 64 ||
            value_[0] == '-' ||
            value_[^1] == '-')
        {
            return false;
        }

        foreach (char character in value_)
        {
            if (!IsLowercaseAsciiLetterOrDigit(
                    character) &&
                character != '-')
            {
                return false;
            }
        }

        return !_reservedNames.Contains(
            value_,
            StringComparer.OrdinalIgnoreCase);
    }

    public static bool TryParse(
        string? value_,
        out SpecId specId_)
    {
        if (!IsValid(
                value_))
        {
            specId_ =
                default;

            return false;
        }

        specId_ =
            new SpecId(
                value_!);

        return true;
    }

    private static bool IsLowercaseAsciiLetterOrDigit(
        char character_)
    {
        return
            character_ >= 'a' &&
            character_ <= 'z' ||
            character_ >= '0' &&
            character_ <= '9';
    }
}
