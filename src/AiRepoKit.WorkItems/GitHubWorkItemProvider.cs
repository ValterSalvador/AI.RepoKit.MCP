namespace AiRepoKit.WorkItems;

using System.Net;
using System.Text.Json;

public sealed class GitHubWorkItemProvider : IWorkItemProvider
{
    private readonly HttpClient _httpClient;

    public GitHubWorkItemProvider(
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
        WorkItemProviderIds.GitHub;

    public async Task<WorkItemSnapshot?> GetAsync(
        WorkItemReference reference_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            reference_);

        ValidateReference(
            reference_);

        string[] namespaceParts =
            reference_.Namespace.Split(
                '/',
                StringSplitOptions.None);

        string requestPath =
            $"repos/{Uri.EscapeDataString(namespaceParts[0])}/{Uri.EscapeDataString(namespaceParts[1])}/issues/{reference_.Id}";

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
                "GitHub response root must be a JSON object.");
        }

        string title =
            GetRequiredString(
                root,
                "title");

        string nativeState =
            GetRequiredString(
                root,
                "state");

        string providerUriValue =
            GetRequiredString(
                root,
                "url");

        Uri providerUri =
            ParseProviderUri(
                providerUriValue);

        WorkItemState state =
            nativeState switch
            {
                "open" => WorkItemState.Open,
                "closed" => WorkItemState.Closed,
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
                WorkItemProviderIds.GitHub,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Reference provider ID must be exactly 'github'.",
                nameof(reference_));
        }

        string[] namespaceParts =
            reference_.Namespace.Split(
                '/',
                StringSplitOptions.None);

        if (namespaceParts.Length != 2 ||
            namespaceParts[0].Length == 0 ||
            namespaceParts[1].Length == 0)
        {
            throw new ArgumentException(
                "GitHub namespace must be exactly 'owner/repository'.",
                nameof(reference_));
        }

        if (!IsCanonicalPositiveInteger(
                reference_.Id))
        {
            throw new ArgumentException(
                "GitHub work-item ID must be a canonical positive base-10 integer.",
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
                "GitHub response is not valid JSON.",
                exception_);
        }
    }

    private static string GetRequiredString(
        JsonElement object_,
        string propertyName_)
    {
        if (!object_.TryGetProperty(
                propertyName_,
                out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException(
                $"GitHub response property '{propertyName_}' must be a string.");
        }

        string? value =
            property.GetString();

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(
                $"GitHub response property '{propertyName_}' must not be blank.");
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
                "GitHub response URL must be an absolute HTTP or HTTPS URI.");
        }

        return uri;
    }
}
