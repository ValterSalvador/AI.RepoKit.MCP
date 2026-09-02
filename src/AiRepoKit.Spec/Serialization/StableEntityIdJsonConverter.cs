using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRepoKit.Spec;

internal sealed class StableEntityIdJsonConverter :
    JsonConverter<StableEntityId>
{
    public override StableEntityId Read(
        ref Utf8JsonReader reader_,
        Type typeToConvert_,
        JsonSerializerOptions options_)
    {
        if (reader_.TokenType !=
            JsonTokenType.String)
        {
            throw new JsonException(
                "Stable entity ID must be a JSON string.");
        }

        string? value =
            reader_.GetString();

        if (!StableEntityId.TryParse(
                value,
                out StableEntityId entityId))
        {
            throw new JsonException(
                $"Invalid stable entity ID '{value}'.");
        }

        return entityId;
    }

    public override void Write(
        Utf8JsonWriter writer_,
        StableEntityId value_,
        JsonSerializerOptions options_)
    {
        if (!StableEntityId.IsValid(
                value_.Value))
        {
            throw new JsonException(
                "Cannot serialize an invalid stable entity ID.");
        }

        writer_.WriteStringValue(
            value_.Value);
    }
}
