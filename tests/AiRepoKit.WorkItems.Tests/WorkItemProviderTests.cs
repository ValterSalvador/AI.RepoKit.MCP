namespace AiRepoKit.WorkItems.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using AiRepoKit.WorkItems;
using Xunit;

public sealed class WorkItemProviderTests
{
    [Theory]
    [InlineData(null, "namespace", "1")]
    [InlineData("", "namespace", "1")]
    [InlineData(" ", "namespace", "1")]
    [InlineData(" github", "namespace", "1")]
    [InlineData("github ", "namespace", "1")]
    [InlineData("github", null, "1")]
    [InlineData("github", "", "1")]
    [InlineData("github", " namespace", "1")]
    [InlineData("github", "namespace", " 1")]
    public void WorkItemReference_RejectsInvalidBaseValues(
        string? providerId_,
        string? namespace_,
        string? id_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new WorkItemReference(
                    providerId_!,
                    namespace_!,
                    id_!));
    }

    [Fact]
    public void WorkItemReference_PreservesExactValues()
    {
        WorkItemReference reference =
            new(
                "Provider",
                "Project-Key",
                "Item-42");

        Assert.Equal(
            "Provider",
            reference.ProviderId);

        Assert.Equal(
            "Project-Key",
            reference.Namespace);

        Assert.Equal(
            "Item-42",
            reference.Id);
    }

    [Fact]
    public void WorkItemSnapshot_RejectsNullReference()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkItemSnapshot(
                    null!,
                    "Title",
                    WorkItemState.Open,
                    "open",
                    new Uri(
                        "https://provider.test/item")));
    }

    [Theory]
    [InlineData(null, "open")]
    [InlineData("", "open")]
    [InlineData(" ", "open")]
    [InlineData("Title", null)]
    [InlineData("Title", "")]
    [InlineData("Title", " ")]
    public void WorkItemSnapshot_RejectsBlankRequiredStrings(
        string? title_,
        string? nativeState_)
    {
        WorkItemReference reference =
            GitHubReference();

        Assert.ThrowsAny<ArgumentException>(
            () =>
                new WorkItemSnapshot(
                    reference,
                    title_!,
                    WorkItemState.Open,
                    nativeState_!,
                    new Uri(
                        "https://provider.test/item")));
    }

    [Fact]
    public void WorkItemSnapshot_RejectsNullProviderUri()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new WorkItemSnapshot(
                    GitHubReference(),
                    "Title",
                    WorkItemState.Open,
                    "open",
                    null!));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://provider.test/item")]
    [InlineData("file:///tmp/item")]
    public void WorkItemSnapshot_RejectsInvalidProviderUris(
        string providerUri_)
    {
        Uri uri =
            new(
                providerUri_,
                UriKind.RelativeOrAbsolute);

        Assert.Throws<ArgumentException>(
            () =>
                new WorkItemSnapshot(
                    GitHubReference(),
                    "Title",
                    WorkItemState.Open,
                    "open",
                    uri));
    }

    [Fact]
    public void WorkItemSnapshot_PreservesExactValues()
    {
        WorkItemReference reference =
            GitHubReference();

        Uri uri =
            new(
                "https://provider.test/item");

        WorkItemSnapshot snapshot =
            new(
                reference,
                "Title with spaces",
                WorkItemState.Closed,
                "native closed",
                uri);

        Assert.Same(
            reference,
            snapshot.Reference);

        Assert.Equal(
            "Title with spaces",
            snapshot.Title);

        Assert.Equal(
            WorkItemState.Closed,
            snapshot.State);

        Assert.Equal(
            "native closed",
            snapshot.NativeState);

        Assert.Equal(
            uri,
            snapshot.ProviderUri);
    }

    [Fact]
    public void GitHubProvider_RejectsNullHttpClient()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new GitHubWorkItemProvider(
                    null!));
    }

    [Fact]
    public void GitHubProvider_RequiresBaseAddress()
    {
        using HttpClient client =
            new(
                new StubHttpMessageHandler(
                    static (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.OK))));

        Assert.Throws<ArgumentException>(
            () =>
                new GitHubWorkItemProvider(
                    client));
    }

    [Fact]
    public void GitHubProvider_ExposesFrozenProviderId()
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    static (_, _) =>
                        Task.FromResult(
                            GitHubResponse(
                                "open"))),
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        Assert.Equal(
            "github",
            provider.ProviderId);
    }

    [Theory]
    [InlineData("open", WorkItemState.Open)]
    [InlineData("closed", WorkItemState.Closed)]
    [InlineData("future", WorkItemState.Unknown)]
    public async Task GitHubProvider_MapsStatesAndUsesFrozenRequest(
        string nativeState_,
        WorkItemState expectedState_)
    {
        StubHttpMessageHandler handler =
            new(
                (_, _) =>
                    Task.FromResult(
                        GitHubResponse(
                            nativeState_)));

        using HttpClient client =
            CreateClient(
                handler,
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        WorkItemSnapshot? snapshot =
            await provider.GetAsync(
                GitHubReference());

        Assert.NotNull(
            snapshot);

        Assert.Equal(
            "github",
            snapshot.Reference.ProviderId);

        Assert.Equal(
            "owner/repo",
            snapshot.Reference.Namespace);

        Assert.Equal(
            "123",
            snapshot.Reference.Id);

        Assert.Equal(
            "GitHub title",
            snapshot.Title);

        Assert.Equal(
            nativeState_,
            snapshot.NativeState);

        Assert.Equal(
            expectedState_,
            snapshot.State);

        Assert.Equal(
            new Uri(
                "https://api.github.test/repos/owner/repo/issues/123"),
            snapshot.ProviderUri);

        Assert.Equal(
            HttpMethod.Get,
            handler.LastMethod);

        Assert.Equal(
            "/repos/owner/repo/issues/123",
            handler.LastRequestUri?.AbsolutePath);

        Assert.Equal(
            1,
            handler.CallCount);
    }

    [Fact]
    public async Task GitHubProvider_IgnoresUnknownMembers()
    {
        string json =
            """
            {
              "title": "GitHub title",
              "state": "open",
              "url": "https://api.github.test/repos/owner/repo/issues/123",
              "unknown": {
                "nested": true
              }
            }
            """;

        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    (_, _) =>
                        Task.FromResult(
                            JsonResponse(
                                json))),
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        WorkItemSnapshot? snapshot =
            await provider.GetAsync(
                GitHubReference());

        Assert.NotNull(
            snapshot);

        Assert.Equal(
            WorkItemState.Open,
            snapshot.State);
    }

    [Fact]
    public async Task GitHubProvider_ReturnsNullFor404()
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    static (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.NotFound))),
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        WorkItemSnapshot? snapshot =
            await provider.GetAsync(
                GitHubReference());

        Assert.Null(
            snapshot);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GitHubProvider_ThrowsForNon404Failure(
        HttpStatusCode statusCode_)
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(
                                statusCode_))),
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () =>
                provider.GetAsync(
                    GitHubReference()));
    }

    [Theory]
    [InlineData("GitHub", "owner/repo", "123")]
    [InlineData("jira", "owner/repo", "123")]
    [InlineData("github", "owner", "123")]
    [InlineData("github", "/repo", "123")]
    [InlineData("github", "owner/", "123")]
    [InlineData("github", "owner/repo/extra", "123")]
    [InlineData("github", "owner//repo", "123")]
    [InlineData("github", "owner/repo", "0")]
    [InlineData("github", "owner/repo", "+1")]
    [InlineData("github", "owner/repo", "01")]
    [InlineData("github", "owner/repo", "-1")]
    [InlineData("github", "owner/repo", "1.0")]
    [InlineData("github", "owner/repo", "abc")]
    public async Task GitHubProvider_RejectsProviderSpecificInvalidReferences(
        string providerId_,
        string namespace_,
        string id_)
    {
        StubHttpMessageHandler handler =
            new(
                static (_, _) =>
                    Task.FromResult(
                        GitHubResponse(
                            "open")));

        using HttpClient client =
            CreateClient(
                handler,
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        WorkItemReference reference =
            new(
                providerId_,
                namespace_,
                id_);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                provider.GetAsync(
                    reference));

        Assert.Equal(
            0,
            handler.CallCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"title\":\"Title\",\"state\":\"open\"}")]
    [InlineData("{\"title\":\"Title\",\"url\":\"https://api.github.test/item\"}")]
    [InlineData("{\"title\":\"\",\"state\":\"open\",\"url\":\"https://api.github.test/item\"}")]
    [InlineData("{\"title\":\"Title\",\"state\":\"open\",\"url\":\"relative\"}")]
    public async Task GitHubProvider_RejectsMalformedOrIncompletePayload(
        string json_)
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    (_, _) =>
                        Task.FromResult(
                            JsonResponse(
                                json_))),
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () =>
                provider.GetAsync(
                    GitHubReference()));
    }

    [Fact]
    public async Task GitHubProvider_PropagatesCancellation()
    {
        StubHttpMessageHandler handler =
            new(
                async (_, cancellationToken_) =>
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken_);

                    return new HttpResponseMessage(
                        HttpStatusCode.OK);
                });

        using HttpClient client =
            CreateClient(
                handler,
                "https://api.github.test/");

        GitHubWorkItemProvider provider =
            new(
                client);

        using CancellationTokenSource cancellation =
            new();

        Task<WorkItemSnapshot?> task =
            provider.GetAsync(
                GitHubReference(),
                cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await task);

        Assert.True(
            handler.LastCancellationToken.CanBeCanceled);

        Assert.True(
            handler.LastCancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void JiraProvider_RejectsNullHttpClient()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new JiraWorkItemProvider(
                    null!));
    }

    [Fact]
    public void JiraProvider_RequiresBaseAddress()
    {
        using HttpClient client =
            new(
                new StubHttpMessageHandler(
                    static (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.OK))));

        Assert.Throws<ArgumentException>(
            () =>
                new JiraWorkItemProvider(
                    client));
    }

    [Fact]
    public void JiraProvider_ExposesFrozenProviderId()
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    static (_, _) =>
                        Task.FromResult(
                            JiraResponse(
                                "new"))),
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        Assert.Equal(
            "jira",
            provider.ProviderId);
    }

    [Theory]
    [InlineData("new", WorkItemState.Open)]
    [InlineData("indeterminate", WorkItemState.Open)]
    [InlineData("done", WorkItemState.Closed)]
    [InlineData("future", WorkItemState.Unknown)]
    public async Task JiraProvider_MapsStatusCategoryAndUsesFrozenRequest(
        string category_,
        WorkItemState expectedState_)
    {
        StubHttpMessageHandler handler =
            new(
                (_, _) =>
                    Task.FromResult(
                        JiraResponse(
                            category_)));

        using HttpClient client =
            CreateClient(
                handler,
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        WorkItemSnapshot? snapshot =
            await provider.GetAsync(
                JiraReference());

        Assert.NotNull(
            snapshot);

        Assert.Equal(
            "jira",
            snapshot.Reference.ProviderId);

        Assert.Equal(
            "ABC",
            snapshot.Reference.Namespace);

        Assert.Equal(
            "ABC-123",
            snapshot.Reference.Id);

        Assert.Equal(
            "Jira title",
            snapshot.Title);

        Assert.Equal(
            "Native status",
            snapshot.NativeState);

        Assert.Equal(
            expectedState_,
            snapshot.State);

        Assert.Equal(
            new Uri(
                "https://jira.test/rest/api/3/issue/ABC-123"),
            snapshot.ProviderUri);

        Assert.Equal(
            HttpMethod.Get,
            handler.LastMethod);

        Assert.Equal(
            "/rest/api/3/issue/ABC-123",
            handler.LastRequestUri?.AbsolutePath);

        Assert.Equal(
            "?fields=summary,status",
            Uri.UnescapeDataString(
                handler.LastRequestUri?.Query ??
                string.Empty));

        Assert.Equal(
            1,
            handler.CallCount);
    }

    [Fact]
    public async Task JiraProvider_IgnoresUnknownMembers()
    {
        string json =
            """
            {
              "self": "https://jira.test/rest/api/3/issue/ABC-123",
              "fields": {
                "summary": "Jira title",
                "status": {
                  "name": "Native status",
                  "statusCategory": {
                    "key": "new",
                    "unknown": true
                  }
                },
                "unknown": 42
              },
              "unknown": "value"
            }
            """;

        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    (_, _) =>
                        Task.FromResult(
                            JsonResponse(
                                json))),
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        WorkItemSnapshot? snapshot =
            await provider.GetAsync(
                JiraReference());

        Assert.NotNull(
            snapshot);

        Assert.Equal(
            WorkItemState.Open,
            snapshot.State);
    }

    [Fact]
    public async Task JiraProvider_ReturnsNullFor404()
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    static (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(
                                HttpStatusCode.NotFound))),
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        WorkItemSnapshot? snapshot =
            await provider.GetAsync(
                JiraReference());

        Assert.Null(
            snapshot);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task JiraProvider_ThrowsForNon404Failure(
        HttpStatusCode statusCode_)
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    (_, _) =>
                        Task.FromResult(
                            new HttpResponseMessage(
                                statusCode_))),
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        await Assert.ThrowsAsync<HttpRequestException>(
            () =>
                provider.GetAsync(
                    JiraReference()));
    }

    [Theory]
    [InlineData("Jira", "ABC", "ABC-123")]
    [InlineData("github", "ABC", "ABC-123")]
    [InlineData("jira", "ABC", "XYZ-123")]
    [InlineData("jira", "ABC", "abc-123")]
    [InlineData("jira", "ABC", "ABC-0")]
    [InlineData("jira", "ABC", "ABC-01")]
    [InlineData("jira", "ABC", "ABC-+1")]
    [InlineData("jira", "ABC", "ABC--1")]
    [InlineData("jira", "ABC", "ABC-X")]
    [InlineData("jira", "ABC", "ABC-1-2")]
    public async Task JiraProvider_RejectsProviderSpecificInvalidReferences(
        string providerId_,
        string namespace_,
        string id_)
    {
        StubHttpMessageHandler handler =
            new(
                static (_, _) =>
                    Task.FromResult(
                        JiraResponse(
                            "new")));

        using HttpClient client =
            CreateClient(
                handler,
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        WorkItemReference reference =
            new(
                providerId_,
                namespace_,
                id_);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                provider.GetAsync(
                    reference));

        Assert.Equal(
            0,
            handler.CallCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"self\":\"https://jira.test/item\",\"fields\":{}}")]
    [InlineData("{\"self\":\"https://jira.test/item\",\"fields\":{\"summary\":\"Title\"}}")]
    [InlineData("{\"self\":\"https://jira.test/item\",\"fields\":{\"summary\":\"Title\",\"status\":{\"name\":\"State\"}}}")]
    [InlineData("{\"self\":\"relative\",\"fields\":{\"summary\":\"Title\",\"status\":{\"name\":\"State\",\"statusCategory\":{\"key\":\"new\"}}}}")]
    [InlineData("{\"self\":\"https://jira.test/item\",\"fields\":{\"summary\":\"\",\"status\":{\"name\":\"State\",\"statusCategory\":{\"key\":\"new\"}}}}")]
    public async Task JiraProvider_RejectsMalformedOrIncompletePayload(
        string json_)
    {
        using HttpClient client =
            CreateClient(
                new StubHttpMessageHandler(
                    (_, _) =>
                        Task.FromResult(
                            JsonResponse(
                                json_))),
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        await Assert.ThrowsAsync<InvalidDataException>(
            () =>
                provider.GetAsync(
                    JiraReference()));
    }

    [Fact]
    public async Task JiraProvider_PropagatesCancellation()
    {
        StubHttpMessageHandler handler =
            new(
                async (_, cancellationToken_) =>
                {
                    await Task.Delay(
                        Timeout.InfiniteTimeSpan,
                        cancellationToken_);

                    return new HttpResponseMessage(
                        HttpStatusCode.OK);
                });

        using HttpClient client =
            CreateClient(
                handler,
                "https://jira.test/");

        JiraWorkItemProvider provider =
            new(
                client);

        using CancellationTokenSource cancellation =
            new();

        Task<WorkItemSnapshot?> task =
            provider.GetAsync(
                JiraReference(),
                cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
                await task);

        Assert.True(
            handler.LastCancellationToken.CanBeCanceled);

        Assert.True(
            handler.LastCancellationToken.IsCancellationRequested);
    }

    private static WorkItemReference GitHubReference()
    {
        return new WorkItemReference(
            "github",
            "owner/repo",
            "123");
    }

    private static WorkItemReference JiraReference()
    {
        return new WorkItemReference(
            "jira",
            "ABC",
            "ABC-123");
    }

    private static HttpClient CreateClient(
        HttpMessageHandler handler_,
        string baseAddress_)
    {
        return new HttpClient(
            handler_)
        {
            BaseAddress =
                new Uri(
                    baseAddress_)
        };
    }

    private static HttpResponseMessage GitHubResponse(
        string state_)
    {
        string json =
            JsonSerializer.Serialize(
                new
                {
                    title = "GitHub title",
                    state = state_,
                    url = "https://api.github.test/repos/owner/repo/issues/123"
                });

        return JsonResponse(
            json);
    }

    private static HttpResponseMessage JiraResponse(
        string category_)
    {
        string json =
            JsonSerializer.Serialize(
                new
                {
                    self = "https://jira.test/rest/api/3/issue/ABC-123",
                    fields = new
                    {
                        summary = "Jira title",
                        status = new
                        {
                            name = "Native status",
                            statusCategory = new
                            {
                                key = category_
                            }
                        }
                    }
                });

        return JsonResponse(
            json);
    }

    private static HttpResponseMessage JsonResponse(
        string json_)
    {
        return new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content =
                new StringContent(
                    json_,
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(
            Func<
                HttpRequestMessage,
                CancellationToken,
                Task<HttpResponseMessage>> handler_)
        {
            _handler =
                handler_;
        }

        public int CallCount { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        public CancellationToken LastCancellationToken { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request_,
            CancellationToken cancellationToken_)
        {
            CallCount++;

            LastMethod =
                request_.Method;

            LastRequestUri =
                request_.RequestUri;

            LastCancellationToken =
                cancellationToken_;

            return _handler(
                request_,
                cancellationToken_);
        }
    }
}
