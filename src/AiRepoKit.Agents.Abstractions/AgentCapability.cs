namespace AiRepoKit.Agents;

public sealed record AgentCapability
{
    public string Value
    {
        get;
    }

    public AgentCapability(
        string value_)
    {
        ArgumentNullException.ThrowIfNull(
            value_,
            nameof(value_));

        if (!IsValid(
                value_))
        {
            throw new ArgumentException(
                "Agent capability must be 1 through 64 lowercase ASCII letters, digits, dots, or hyphens, and cannot start or end with a dot or hyphen.",
                nameof(value_));
        }

        this.Value =
            value_;
    }

    public override string ToString()
    {
        return this.Value;
    }

    private static bool IsValid(
        string value_)
    {
        if (value_.Length is < 1 or > 64 ||
            value_[0] == '.' ||
            value_[0] == '-' ||
            value_[^1] == '.' ||
            value_[^1] == '-')
        {
            return false;
        }

        foreach (char character in value_)
        {
            if (!IsLowercaseAsciiLetterOrDigit(
                    character) &&
                character != '.' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowercaseAsciiLetterOrDigit(
        char character_)
    {
        return
            character_ is >= 'a' and <= 'z' ||
            character_ is >= '0' and <= '9';
    }
}
