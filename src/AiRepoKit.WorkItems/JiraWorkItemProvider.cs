namespace AiRepoKit.WorkItems;

using System.Net;
using System.Text.Json;

public sealed class JiraWorkItemProvider : IWorkItemProvider
{
    private readonly HttpClient _httpClient;

    public JiraWorkItemProvider(
        HttpClient httpClient_)
    {
        ArgumentNullException.ThrowIfNull(
            httpClient_);

        if (httpClient_.BaseAddress is null ||
            !httpClient_.BaseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException(
                "HttpClient.BaseAddress must be an absolute URI.",
                nameof(httpClient_));
        }

        _httpClient = httpClient_;
    }

    public string ProviderId =>
        WorkItemProviderIds.Jira;

    public async Task<WorkItemSnapshot?> GetAsync(
        WorkItemReference reference_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            reference_);

        ValidateReference(
            reference_);

        string requestPath =
            $"rest/api/3/issue/{Uri.EscapeDataString(reference_.Id)}?fields=summary,status";

        using HttpRequestMessage request =
            new(
                HttpMethod.Get,
                requestPath);

        using HttpResponseMessage response =
            await _httpClient
                .SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken_)
                .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        using JsonDocument document =
            await ReadDocumentAsync(
                    response.Content,
                    cancellationToken_)
                .ConfigureAwait(false);

        JsonElement root =
            document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                "Jira response root must be a JSON object.");
        }

        string providerUriValue =
            GetRequiredString(
                root,
                "self",
                "Jira response");

        JsonElement fields =
            GetRequiredObject(
                root,
                "fields",
                "Jira response");

        string title =
            GetRequiredString(
                fields,
                "summary",
                "Jira response fields");

        JsonElement status =
            GetRequiredObject(
                fields,
                "status",
                "Jira response fields");

        string nativeState =
            GetRequiredString(
                status,
                "name",
                "Jira response status");

        JsonElement statusCategory =
            GetRequiredObject(
                status,
                "statusCategory",
                "Jira response status");

        string categoryKey =
            GetRequiredString(
                statusCategory,
                "key",
                "Jira response status category");

        Uri providerUri =
            ParseProviderUri(
                providerUriValue);

        WorkItemState state =
            categoryKey switch
            {
                "new" => WorkItemState.Open,
                "indeterminate" => WorkItemState.Open,
                "done" => WorkItemState.Closed,
                _ => WorkItemState.Unknown
            };

        return new WorkItemSnapshot(
            reference_,
            title,
            state,
            nativeState,
            providerUri);
    }

    private static void ValidateReference(
        WorkItemReference reference_)
    {
        if (!string.Equals(
                reference_.ProviderId,
                WorkItemProviderIds.Jira,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Reference provider ID must be exactly 'jira'.",
                nameof(reference_));
        }

        string prefix =
            reference_.Namespace + "-";

        if (!reference_.Id.StartsWith(
                prefix,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Jira issue key must start with the exact project key.",
                nameof(reference_));
        }

        string numericPart =
            reference_.Id[
                prefix.Length..];

        if (!IsCanonicalPositiveInteger(
                numericPart))
        {
            throw new ArgumentException(
                "Jira issue key suffix must be a canonical positive base-10 integer.",
                nameof(reference_));
        }
    }

    private static bool IsCanonicalPositiveInteger(
        string value_)
    {
        if (value_.Length == 0 ||
            value_[0] < '1' ||
            value_[0] > '9')
        {
            return false;
        }

        for (int index = 1; index < value_.Length; index++)
        {
            if (value_[index] < '0' ||
                value_[index] > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(
        HttpContent content_,
        CancellationToken cancellationToken_)
    {
        try
        {
            using Stream stream =
                await content_
                    .ReadAsStreamAsync(
                        cancellationToken_)
                    .ConfigureAwait(false);

            return await JsonDocument
                .ParseAsync(
                    stream,
                    cancellationToken:
                        cancellationToken_)
                .ConfigureAwait(false);
        }
        catch (JsonException exception_)
        {
            throw new InvalidDataException(
                "Jira response is not valid JSON.",
                exception_);
        }
    }

    private static JsonElement GetRequiredObject(
        JsonElement object_,
        string propertyName_,
        string context_)
    {
        if (!object_.TryGetProperty(
                propertyName_,
                out JsonElement property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"{context_} property '{propertyName_}' must be an object.");
        }

        return property;
    }

    private static string GetRequiredString(
        JsonElement object_,
        string propertyName_,
        string context_)
    {
        if (!object_.TryGetProperty(
                propertyName_,
                out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"{context_} property '{propertyName_}' must be a string.");
        }

        string? value =
            property.GetString();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"{context_} property '{propertyName_}' must not be blank.");
        }

        return value;
    }

    private static Uri ParseProviderUri(
        string value_)
    {
        if (!Uri.TryCreate(
                value_,
                UriKind.Absolute,
                out Uri? uri) ||
            (!string.Equals(
                 uri.Scheme,
                 Uri.UriSchemeHttp,
                 StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(
                 uri.Scheme,
                 Uri.UriSchemeHttps,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Jira response self URI must be an absolute HTTP or HTTPS URI.");
        }

        return uri;
    }
}
