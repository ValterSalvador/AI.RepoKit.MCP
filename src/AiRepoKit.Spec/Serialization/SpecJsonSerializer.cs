using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace AiRepoKit.Spec;

public static class SpecJsonSerializer
{
    private static readonly JsonSerializerOptions _options =
        CreateOptions();

    public static string Serialize<T>(
        T value_)
    {
        ArgumentNullException.ThrowIfNull(
            value_);

        return JsonSerializer.Serialize(
            value_,
            _options);
    }

    public static T Deserialize<T>(
        string json_)
    {
        ArgumentNullException.ThrowIfNull(
            json_);

        T? value =
            JsonSerializer.Deserialize<T>(
                json_,
                _options);

        if (value is null)
        {
            throw new JsonException(
                $"JSON payload did not contain a '{typeof(T).Name}' value.");
        }

        return value;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        DefaultJsonTypeInfoResolver resolver =
            new();

        resolver.Modifiers.Add(
            SortProperties);

        JsonSerializerOptions options =
            new()
            {
                PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling =
                    JsonUnmappedMemberHandling.Disallow,
                TypeInfoResolver =
                    resolver,
                WriteIndented =
                    false
            };

        options.Converters.Add(
            new StableEntityIdJsonConverter());

        options.Converters.Add(
            new ArtifactRevisionJsonConverter());

        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));

        options.MakeReadOnly();

        return options;
    }

    private static void SortProperties(
        JsonTypeInfo typeInfo_)
    {
        if (typeInfo_.Kind !=
            JsonTypeInfoKind.Object)
        {
            return;
        }

        JsonPropertyInfo[] orderedProperties =
            typeInfo_
                .Properties
                .OrderBy(
                    property_ =>
                        property_.Name,
                    StringComparer.Ordinal)
                .ToArray();

        typeInfo_.Properties.Clear();

        foreach (JsonPropertyInfo property in
                 orderedProperties)
        {
            typeInfo_.Properties.Add(
                property);
        }
    }
}
