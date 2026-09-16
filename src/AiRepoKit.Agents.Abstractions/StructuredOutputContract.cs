namespace AiRepoKit.Agents;

using System.Text.Json;

public sealed record StructuredOutputContract
{
    public string JsonSchema
    {
        get;
    }

    public StructuredOutputContract(
        string jsonSchema_)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            jsonSchema_,
            nameof(jsonSchema_));

        try
        {
            using JsonDocument document =
                JsonDocument.Parse(
                    jsonSchema_);
        }
        catch (JsonException exception_)
        {
            throw new ArgumentException(
                "Supplied JSON schema must be syntactically valid JSON.",
                nameof(jsonSchema_),
                exception_);
        }

        this.JsonSchema =
            jsonSchema_;
    }
}
