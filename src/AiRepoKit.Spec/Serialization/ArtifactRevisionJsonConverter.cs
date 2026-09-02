using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiRepoKit.Spec;

internal sealed class ArtifactRevisionJsonConverter :
    JsonConverter<ArtifactRevision>
{
    public override ArtifactRevision Read(
        ref Utf8JsonReader reader_,
        Type typeToConvert_,
        JsonSerializerOptions options_)
    {
        if (reader_.TokenType !=
            JsonTokenType.Number ||
            !reader_.TryGetInt32(
                out int value))
        {
            throw new JsonException(
                "Artifact revision must be a JSON integer.");
        }

        try
        {
            return new ArtifactRevision(
                value);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new JsonException(
                "Artifact revision must be greater than zero.",
                exception);
        }
    }

    public override void Write(
        Utf8JsonWriter writer_,
        ArtifactRevision value_,
        JsonSerializerOptions options_)
    {
        if (!value_.IsValid)
        {
            throw new JsonException(
                "Cannot serialize an invalid artifact revision.");
        }

        writer_.WriteNumberValue(
            value_.Value);
    }
}
