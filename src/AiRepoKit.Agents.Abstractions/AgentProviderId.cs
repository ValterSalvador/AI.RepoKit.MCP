namespace AiRepoKit.Agents;

using System.Diagnostics.CodeAnalysis;

public sealed record AgentProviderId
{
    public string Value
    {
        get;
    }

    public AgentProviderId(
        string value_)
    {
        if (!IsValid(
                value_))
        {
            throw new ArgumentException(
                "Agent provider ID must be 1 through 64 lowercase ASCII letters, digits, dots, or hyphens, and cannot start or end with a dot or hyphen.",
                nameof(value_));
        }

        this.Value =
            value_;
    }

    public override string ToString()
    {
        return this.Value;
    }

    public static bool IsValid(
        [NotNullWhen(true)] string? value_)
    {
        if (string.IsNullOrEmpty(
                value_) ||
            value_.Length > 64 ||
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

    public static bool TryParse(
        string? value_,
        [NotNullWhen(true)] out AgentProviderId? providerId_)
    {
        if (!IsValid(
                value_))
        {
            providerId_ =
                null;

            return false;
        }

        providerId_ =
            new AgentProviderId(
                value_);

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
