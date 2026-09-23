namespace AiRepoKit.Agents.Runtime.Tests;

using System.Runtime.CompilerServices;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ResilientModelExecutionRuntimeTests
{
    private static readonly AgentCapabilitySet DefaultCapabilities = new([new AgentCapability("text-generation")]);

    private static ModelRouteCandidate CreateCandidate(
        string modelId_,
        IModelExecutionRuntime runtime_,
        ModelHealthStatus healthStatus_ = ModelHealthStatus.Healthy)
    {
        ModelRuntimeRegistration registration = new(
            new AgentProviderId("antigravity"),
            modelId_,
            DefaultCapabilities,
            runtime_);

        return new ModelRouteCandidate(registration, healthStatus_);
    }

    [Fact]
    public void Constructor_Public_NullRoutingRuntime_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "routingRuntime_",
            () => new ResilientModelExecutionRuntime(
                null!,
                DefaultCapabilities,
                new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
                new TestClassifier()));
    }

    [Fact]
    public void Constructor_Public_NullRequiredCapabilities_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "requiredCapabilities_",
            () => new ResilientModelExecutionRuntime(
                new TestRoutingRuntime(),
                null!,
                new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
                new TestClassifier()));
    }

    [Fact]
    public void Constructor_Public_NullResiliencePolicy_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "resiliencePolicy_",
            () => new ResilientModelExecutionRuntime(
                new TestRoutingRuntime(),
                DefaultCapabilities,
                null!,
                new TestClassifier()));
    }

    [Fact]
    public void Constructor_Public_NullFailureClassifier_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "failureClassifier_",
            () => new ResilientModelExecutionRuntime(
                new TestRoutingRuntime(),
                DefaultCapabilities,
                new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
                null!));
    }

    [Fact]
    public void Constructor_Internal_NullTimeProvider_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            "timeProvider_",
            () => new ResilientModelExecutionRuntime(
                new TestRoutingRuntime(),
                DefaultCapabilities,
                new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
                new TestClassifier(),
                null!));
    }

    [Fact]
    public async Task ExecuteAsync_NullRequest_ThrowsArgumentNullException()
    {
        ResilientModelExecutionRuntime runtime = new(
            new TestRoutingRuntime(),
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        await Assert.ThrowsAsync<ArgumentNullException>(
            "request_",
            () => runtime.ExecuteAsync(null!));
    }

    [Fact]
    public async Task ExecuteAsync_RoutesOnce_PassesExactCapabilitiesAndToken()
    {
        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        TestCandidateRuntime candidate = new();
        candidate.ExecuteAsyncFunc = _ => Task.FromResult(new ModelExecutionResult("ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (caps, tok) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        ModelExecutionRequest request = new("test prompt");
        ModelExecutionResult result = await runtime.ExecuteAsync(request, token);

        Assert.Equal("ok", result.ResponseText);
        Assert.Equal(1, routing.RouteCallCount);
        Assert.Same(DefaultCapabilities, routing.LastRequiredCapabilities);
        Assert.Equal(token, routing.LastCancellationToken);
        Assert.Same(request, candidate.LastExecuteRequest);
        Assert.Equal(token, candidate.LastExecuteToken);
    }

    [Fact]
    public async Task ExecuteAsync_NullRoute_ThrowsInvalidOperationException()
    {
        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(null!);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyRoute_ThrowsInvalidOperationException()
    {
        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));
    }

    [Fact]
    public async Task ExecuteAsync_NullCandidateElement_ThrowsInvalidOperationException()
    {
        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([null!]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));
    }

    [Fact]
    public async Task ExecuteAsync_FirstAttemptSuccess_ReturnsExactResultAndTelemetry()
    {
        ModelExecutionTelemetry telemetry = new(new ModelTokenUsage(10, 20), TimeSpan.FromMilliseconds(100), 0.05m, "USD");
        ModelExecutionResult expectedResult = new("hello world", telemetry);

        TestCandidateRuntime candidate = new();
        candidate.ExecuteAsyncFunc = _ => Task.FromResult(expectedResult);

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            new TestClassifier());

        ModelExecutionResult actual = await runtime.ExecuteAsync(new ModelExecutionRequest("test"));

        Assert.Same(expectedResult, actual);
        Assert.Same(telemetry, actual.Telemetry);
        Assert.Equal(1, candidate.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_MaxAttemptsOne_DoesNotRetryOnFailure()
    {
        TestCandidateRuntime candidate = new();
        candidate.ExecuteAsyncFunc = _ => throw new HttpRequestException("fail");

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            classifier);

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));

        Assert.Equal("fail", ex.Message);
        Assert.Equal(1, candidate.ExecuteAsyncCount);
        Assert.Equal(1, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableFailure_RetriesSameCandidate_WithBackoff()
    {
        ManualTimeProvider timeProvider = new();
        TestCandidateRuntime candidate = new();
        int callCount = 0;
        ModelExecutionResult successResult = new("recovered", new ModelExecutionTelemetry(null, TimeSpan.Zero));

        candidate.ExecuteAsyncFunc = _ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new TimeoutException("timeout 1");
            }
            return Task.FromResult(successResult);
        };

        TestRoutingRuntime routing = new();
        ModelRouteCandidate routeCandidate = CreateCandidate("m1", candidate);
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([routeCandidate]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.FromMilliseconds(500)),
            classifier,
            timeProvider);

        Task<ModelExecutionResult> executeTask = runtime.ExecuteAsync(new ModelExecutionRequest("test"));

        // Advance time to allow backoff delay to complete
        timeProvider.Advance(TimeSpan.FromMilliseconds(500));

        ModelExecutionResult result = await executeTask;

        Assert.Same(successResult, result);
        Assert.Equal(2, candidate.ExecuteAsyncCount);
        Assert.Equal(1, classifier.InvocationCount);
        Assert.Same(routeCandidate, classifier.LastCandidate);
        Assert.IsType<TimeoutException>(classifier.LastException);
    }

    [Fact]
    public async Task ExecuteAsync_ZeroBackoff_RetriesWithoutTimer()
    {
        TestCandidateRuntime candidate = new();
        int callCount = 0;
        ModelExecutionResult successResult = new("recovered", new ModelExecutionTelemetry(null, TimeSpan.Zero));

        candidate.ExecuteAsyncFunc = _ =>
        {
            callCount++;
            if (callCount == 1)
            {
                throw new HttpRequestException("transient");
            }
            return Task.FromResult(successResult);
        };

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("test"));

        Assert.Same(successResult, result);
        Assert.Equal(2, candidate.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_ExhaustedCandidate_FallsBackImmediately_InRouteOrder()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteAsyncFunc = _ => throw new HttpRequestException("cand 1 fail");

        TestCandidateRuntime candidate2 = new();
        ModelExecutionResult successResult = new("cand 2 success", new ModelExecutionTelemetry(null, TimeSpan.Zero));
        candidate2.ExecuteAsyncFunc = _ => Task.FromResult(successResult);

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("test"));

        Assert.Same(successResult, result);
        Assert.Equal(2, candidate1.ExecuteAsyncCount);
        Assert.Equal(1, candidate2.ExecuteAsyncCount);
        Assert.Equal(1, routing.RouteCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_NonRetryableFailure_DoesNotFallback()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteAsyncFunc = _ => throw new InvalidOperationException("terminal error");

        TestCandidateRuntime candidate2 = new();
        candidate2.ExecuteAsyncFunc = _ => Task.FromResult(new ModelExecutionResult("ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = false };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));

        Assert.Equal("terminal error", ex.Message);
        Assert.Equal(1, candidate1.ExecuteAsyncCount);
        Assert.Equal(0, candidate2.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_FinalCandidateExhaustion_ThrowsFinalCandidateException()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteAsyncFunc = _ => throw new HttpRequestException("c1");

        TestCandidateRuntime candidate2 = new();
        candidate2.ExecuteAsyncFunc = _ => throw new TimeoutException("c2 timeout");

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            classifier);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));

        Assert.Equal("c2 timeout", ex.Message);
        Assert.Equal(1, candidate1.ExecuteAsyncCount);
        Assert.Equal(1, candidate2.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancelledBeforeInvocation_CandidateNotCalled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        TestCandidateRuntime candidate = new();
        TestRoutingRuntime routing = new();

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            new TestClassifier());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test"), cts.Token));

        Assert.Equal(0, routing.RouteCallCount);
        Assert.Equal(0, candidate.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationDuringBackoff_InterruptsRetry()
    {
        ManualTimeProvider timeProvider = new();
        using CancellationTokenSource cts = new();

        TestCandidateRuntime candidate = new();
        candidate.ExecuteAsyncFunc = _ => throw new HttpRequestException("transient");

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(3, TimeSpan.FromSeconds(10)),
            classifier,
            timeProvider);

        Task<ModelExecutionResult> task = runtime.ExecuteAsync(new ModelExecutionRequest("test"), cts.Token);

        // Cancel during backoff
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, candidate.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_CandidateOperationCanceledException_IsTerminal_NotClassified()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteAsyncFunc = _ => throw new OperationCanceledException("candidate canceled");

        TestCandidateRuntime candidate2 = new();
        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(3, TimeSpan.Zero),
            classifier);

        OperationCanceledException ex = await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));

        Assert.Equal("candidate canceled", ex.Message);
        Assert.Equal(1, candidate1.ExecuteAsyncCount);
        Assert.Equal(0, candidate2.ExecuteAsyncCount);
        Assert.Equal(0, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_NullCandidateResult_ThrowsInvalidOperationException_NotClassified()
    {
        TestCandidateRuntime candidate = new();
        candidate.ExecuteAsyncFunc = _ => Task.FromResult<ModelExecutionResult>(null!);

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));

        Assert.Equal(1, candidate.ExecuteAsyncCount);
        Assert.Equal(0, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_ClassifierException_IsTerminal()
    {
        TestCandidateRuntime candidate = new();
        candidate.ExecuteAsyncFunc = _ => throw new HttpRequestException("fail");

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { ThrowOnIsRetryable = new FormatException("classifier failed") };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(3, TimeSpan.Zero),
            classifier);

        FormatException ex = await Assert.ThrowsAsync<FormatException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("test")));

        Assert.Equal("classifier failed", ex.Message);
        Assert.Equal(1, candidate.ExecuteAsyncCount);
    }

    [Fact]
    public async Task ExecuteAsync_SameRequestAndTimeoutRetainedAcrossAttempts()
    {
        ModelExecutionRequest request = new("prompt with timeout", null, TimeSpan.FromSeconds(5));
        List<ModelExecutionRequest> capturedRequests = [];

        TestCandidateRuntime candidate = new();
        int attempts = 0;
        candidate.ExecuteAsyncFunc = req =>
        {
            capturedRequests.Add(req);
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("timeout");
            }
            return Task.FromResult(new ModelExecutionResult("ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        };

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        await runtime.ExecuteAsync(request);

        Assert.Equal(2, capturedRequests.Count);
        Assert.Same(request, capturedRequests[0]);
        Assert.Same(request, capturedRequests[1]);
        Assert.Equal(TimeSpan.FromSeconds(5), capturedRequests[0].Timeout);
        Assert.Equal(TimeSpan.FromSeconds(5), capturedRequests[1].Timeout);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_RoutingOccursWhenEnumerationBegins()
    {
        TestRoutingRuntime routing = new();
        TestCandidateRuntime candidate = new();
        candidate.StreamingUpdates = [new ModelExecutionUpdate("part1")];
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        IAsyncEnumerable<ModelExecutionUpdate> stream = runtime.ExecuteStreamingAsync(new ModelExecutionRequest("prompt"));

        // RouteAsync must NOT have been called yet
        Assert.Equal(0, routing.RouteCallCount);

        // Enumerate
        List<ModelExecutionUpdate> received = [];
        await foreach (ModelExecutionUpdate update in stream)
        {
            received.Add(update);
        }

        Assert.Equal(1, routing.RouteCallCount);
        Assert.Single(received);
        Assert.Equal("part1", received[0].ResponseTextDelta);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_SkipsNonStreamingCandidates_PreservesOrder()
    {
        PlainExecutionOnlyRuntime nonStreamingCandidate = new();
        TestCandidateRuntime streamingCandidate = new();
        streamingCandidate.StreamingUpdates = [new ModelExecutionUpdate("streamed")];

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", nonStreamingCandidate),
            CreateCandidate("m2", streamingCandidate)
        ]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("prompt")))
        {
            updates.Add(update);
        }

        Assert.Single(updates);
        Assert.Equal("streamed", updates[0].ResponseTextDelta);
        Assert.Equal(1, streamingCandidate.ExecuteStreamingAsyncCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_NoEligibleStreamingCandidates_ThrowsInvalidOperationException()
    {
        PlainExecutionOnlyRuntime nonStreamingCandidate = new();

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", nonStreamingCandidate)
        ]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        IAsyncEnumerable<ModelExecutionUpdate> stream = runtime.ExecuteStreamingAsync(new ModelExecutionRequest("prompt"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in stream)
            {
            }
        });
    }

    [Fact]
    public async Task ExecuteStreamingAsync_PreservesUpdateObjectIdentity()
    {
        ModelExecutionUpdate u1 = new("delta");
        ModelExecutionUpdate u2 = new(string.Empty);
        ModelExecutionUpdate u3 = new(string.Empty, new ModelExecutionTelemetry(new ModelTokenUsage(5, 5), TimeSpan.FromMilliseconds(50), 0.01m, "USD"));

        TestCandidateRuntime candidate = new();
        candidate.StreamingUpdates = [u1, u2, u3];

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
        {
            received.Add(update);
        }

        Assert.Equal(3, received.Count);
        Assert.Same(u1, received[0]);
        Assert.Same(u2, received[1]);
        Assert.Same(u3, received[2]);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FailureBeforeFirstUpdate_RetriesSameCandidate()
    {
        TestCandidateRuntime candidate = new();
        ModelExecutionUpdate expectedUpdate = new("recovered stream");
        int attempt = 0;

        candidate.ExecuteStreamingAsyncFunc = () =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new HttpRequestException("stream start fail");
            }
            return CreateAsyncEnumerable([expectedUpdate]);
        };

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        List<ModelExecutionUpdate> received = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
        {
            received.Add(update);
        }

        Assert.Single(received);
        Assert.Same(expectedUpdate, received[0]);
        Assert.Equal(2, candidate.ExecuteStreamingAsyncCount);
        Assert.Equal(1, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ExhaustedFailureBeforeFirstUpdate_FallsBack()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteStreamingAsyncFunc = () => throw new HttpRequestException("cand 1 fail");

        TestCandidateRuntime candidate2 = new();
        ModelExecutionUpdate expectedUpdate = new("cand 2 stream");
        candidate2.StreamingUpdates = [expectedUpdate];

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        List<ModelExecutionUpdate> received = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
        {
            received.Add(update);
        }

        Assert.Single(received);
        Assert.Same(expectedUpdate, received[0]);
        Assert.Equal(2, candidate1.ExecuteStreamingAsyncCount);
        Assert.Equal(1, candidate2.ExecuteStreamingAsyncCount);
    }

    [Theory]
    [InlineData("delta")]
    [InlineData("")]
    [InlineData("telemetry")]
    public async Task ExecuteStreamingAsync_FailureAfterExposedUpdate_PropagatesImmediately_NoRetryOrFallback(string updateType_)
    {
        ModelExecutionUpdate exposedUpdate = updateType_ switch
        {
            "delta" => new ModelExecutionUpdate("non-empty"),
            "" => new ModelExecutionUpdate(string.Empty),
            "telemetry" => new ModelExecutionUpdate(string.Empty, new ModelExecutionTelemetry(null, TimeSpan.Zero)),
            _ => throw new ArgumentException("Unknown type")
        };

        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteStreamingAsyncFunc = () => CreateAsyncEnumerableWithFailure([exposedUpdate], new IOException("mid-stream disconnect"));

        TestCandidateRuntime candidate2 = new();
        candidate2.StreamingUpdates = [new ModelExecutionUpdate("fallback update")];

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        List<ModelExecutionUpdate> received = [];
        IOException ex = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
            {
                received.Add(update);
            }
        });

        Assert.Equal("mid-stream disconnect", ex.Message);
        Assert.Single(received);
        Assert.Same(exposedUpdate, received[0]);
        Assert.Equal(1, candidate1.ExecuteStreamingAsyncCount);
        Assert.Equal(0, candidate2.ExecuteStreamingAsyncCount);
        Assert.Equal(0, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ZeroUpdateStream_SucceedsWithoutFallback()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.StreamingUpdates = []; // zero updates

        TestCandidateRuntime candidate2 = new();
        candidate2.StreamingUpdates = [new ModelExecutionUpdate("cand2")];

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
        {
            received.Add(update);
        }

        Assert.Empty(received);
        Assert.Equal(1, candidate1.ExecuteStreamingAsyncCount);
        Assert.Equal(0, candidate2.ExecuteStreamingAsyncCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_NullYieldedUpdate_ThrowsInvalidOperationException_NotClassified()
    {
        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => CreateAsyncEnumerable([null!]);

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
            {
            }
        });

        Assert.Equal(1, candidate.ExecuteStreamingAsyncCount);
        Assert.Equal(0, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_DisposalFailureBeforeFirstUpdate_BehavesAsCandidateFailure()
    {
        TestCandidateRuntime candidate = new();
        int attempt = 0;
        ModelExecutionUpdate expected = new("recovered from disposal fail");

        candidate.ExecuteStreamingAsyncFunc = () =>
        {
            attempt++;
            if (attempt == 1)
            {
                return new CustomDisposableAsyncEnumerable([], new IOException("dispose fail"));
            }
            return CreateAsyncEnumerable([expected]);
        };

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        List<ModelExecutionUpdate> received = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
        {
            received.Add(update);
        }

        Assert.Single(received);
        Assert.Same(expected, received[0]);
        Assert.Equal(2, candidate.ExecuteStreamingAsyncCount);
        Assert.Equal(1, classifier.InvocationCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FailureAfterExposedUpdate_DisposalAlsoFails_PreservesPrimaryFailure()
    {
        ModelExecutionUpdate update1 = new("part1");
        int moveNextCount = 0;
        bool disposalCalled = false;
        InvalidOperationException primaryEx = new("primary mid-stream failure");
        IOException disposalEx = new("disposal failure");

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    throw primaryEx;
                },
                currentFunc_: () => update1,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    throw disposalEx;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
            {
                received.Add(update);
            }
        });

        Assert.Same(primaryEx, actual);
        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);
    }

    [Fact]
    public async Task Session_StreamingFailureAfterExposedUpdate_DisposalAlsoFails_PreservesPrimaryFailure()
    {
        ModelExecutionUpdate update1 = new("part1");
        int moveNextCount = 0;
        bool disposalCalled = false;
        InvalidOperationException primaryEx = new("primary session streaming failure");
        IOException disposalEx = new("disposal failure");

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingInSessionAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    throw primaryEx;
                },
                currentFunc_: () => update1,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    throw disposalEx;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();

        List<ModelExecutionUpdate> received = [];
        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(session, new ModelExecutionRequest("test")))
            {
                received.Add(update);
            }
        });

        Assert.Same(primaryEx, actual);
        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);

        candidate.EndSessionResult = true;
        bool ended = await runtime.EndSessionAsync(session);
        Assert.True(ended);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_CallerCancellationDuringMoveNext_CandidateIgnoresToken_CancellationWins()
    {
        using CancellationTokenSource cts = new();
        ModelExecutionUpdate update1 = new("first");
        ModelExecutionUpdate update2 = new("unwanted-second");
        int moveNextCount = 0;
        bool disposalCalled = false;

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    if (moveNextCount == 2)
                    {
                        cts.Cancel();
                        return ValueTask.FromResult(true);
                    }

                    return ValueTask.FromResult(false);
                },
                currentFunc_: () => moveNextCount == 1 ? update1 : update2,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    return ValueTask.CompletedTask;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test"), cts.Token))
            {
                received.Add(update);
            }
        });

        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);
        Assert.Equal(2, moveNextCount);
    }

    [Fact]
    public async Task Session_StreamingCallerCancellationDuringMoveNext_CandidateIgnoresToken_CancellationWins()
    {
        using CancellationTokenSource cts = new();
        ModelExecutionUpdate update1 = new("first");
        ModelExecutionUpdate update2 = new("unwanted-second");
        int moveNextCount = 0;
        bool disposalCalled = false;

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingInSessionAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    if (moveNextCount == 2)
                    {
                        cts.Cancel();
                        return ValueTask.FromResult(true);
                    }

                    return ValueTask.FromResult(false);
                },
                currentFunc_: () => moveNextCount == 1 ? update1 : update2,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    return ValueTask.CompletedTask;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();

        List<ModelExecutionUpdate> received = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(session, new ModelExecutionRequest("test"), cts.Token))
            {
                received.Add(update);
            }
        });

        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);

        candidate.EndSessionResult = true;
        bool ended = await runtime.EndSessionAsync(session);
        Assert.True(ended);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_CallerCancellationDuringMoveNext_CandidateReturnsFalse_CancellationWins()
    {
        using CancellationTokenSource cts = new();
        ModelExecutionUpdate update1 = new("first");
        int moveNextCount = 0;
        bool disposalCalled = false;

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    if (moveNextCount == 2)
                    {
                        cts.Cancel();
                        return ValueTask.FromResult(false);
                    }

                    return ValueTask.FromResult(false);
                },
                currentFunc_: () => update1,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    return ValueTask.CompletedTask;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test"), cts.Token))
            {
                received.Add(update);
            }
        });

        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_DisposalFailureDuringCancellation_PreservesCancellation()
    {
        using CancellationTokenSource cts = new();
        ModelExecutionUpdate update1 = new("first");
        int moveNextCount = 0;
        bool disposalCalled = false;
        IOException disposalEx = new("disposal failure during cancellation");

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    cts.Cancel();
                    return ValueTask.FromResult(true);
                },
                currentFunc_: () => update1,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    throw disposalEx;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test"), cts.Token))
            {
                received.Add(update);
            }
        });

        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_CleanStream_DisposalFails_PropagatesDisposalFailure()
    {
        ModelExecutionUpdate update1 = new("first");
        int moveNextCount = 0;
        bool disposalCalled = false;
        IOException disposalEx = new("standalone disposal failure");

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    moveNextCount++;
                    if (moveNextCount == 1)
                    {
                        return ValueTask.FromResult(true);
                    }

                    return ValueTask.FromResult(false);
                },
                currentFunc_: () => update1,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    throw disposalEx;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        IOException actual = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test")))
            {
                received.Add(update);
            }
        });

        Assert.Same(disposalEx, actual);
        Assert.Single(received);
        Assert.Same(update1, received[0]);
        Assert.True(disposalCalled);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FirstMoveNext_CallerCancellation_CandidateIgnoresToken_UpdateNeverExposed()
    {
        using CancellationTokenSource cts = new();
        ModelExecutionUpdate update1 = new("unwanted-first");
        bool disposalCalled = false;

        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingAsyncFunc = () => new ScriptedStreamingAsyncEnumerable(() =>
            new ScriptedStreamingEnumerator(
                moveNextFunc_: () =>
                {
                    cts.Cancel();
                    return ValueTask.FromResult(true);
                },
                currentFunc_: () => update1,
                disposeFunc_: () =>
                {
                    disposalCalled = true;
                    return ValueTask.CompletedTask;
                }));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        List<ModelExecutionUpdate> received = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("test"), cts.Token))
            {
                received.Add(update);
            }
        });

        Assert.Empty(received);
        Assert.True(disposalCalled);
    }

    [Fact]
    public void CreateSession_ZeroRouteCalls_ZeroCandidateCalls_ReturnsUniqueOpaqueReference()
    {
        TestRoutingRuntime routing = new();
        TestCandidateRuntime candidate = new();

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference s1 = runtime.CreateSession();
        AgentSessionReference s2 = runtime.CreateSession();

        Assert.NotNull(s1);
        Assert.NotNull(s2);
        Assert.False(string.IsNullOrWhiteSpace(s1.Value));
        Assert.False(string.IsNullOrWhiteSpace(s2.Value));
        Assert.NotEqual(s1.Value, s2.Value);

        Assert.DoesNotContain("antigravity", s1.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("m1", s1.Value, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, routing.RouteCallCount);
        Assert.Equal(0, candidate.CreateSessionCount);
    }

    [Fact]
    public async Task Session_UnknownSession_ExecuteThrows_EndReturnsFalse()
    {
        TestRoutingRuntime routing = new();
        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference unknown = new("unknown-session-id");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(unknown, new ModelExecutionRequest("test")));

        Assert.Throws<InvalidOperationException>(
            () => runtime.ExecuteStreamingInSessionAsync(unknown, new ModelExecutionRequest("test")));

        bool endResult = await runtime.EndSessionAsync(unknown);
        Assert.False(endResult);
        Assert.Equal(0, routing.RouteCallCount);
    }

    [Fact]
    public async Task Session_FirstExecution_LazyBindsStickily_NeverRoutesAgain()
    {
        PlainExecutionOnlyRuntime plainRuntime = new();
        TestCandidateRuntime resumableCandidate = new();
        resumableCandidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("res1", new ModelExecutionTelemetry(null, TimeSpan.Zero)));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("plain", plainRuntime),
            CreateCandidate("resumable", resumableCandidate)
        ]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();
        Assert.Equal(0, routing.RouteCallCount);

        // Turn 1: lazy binds
        ModelExecutionResult r1 = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("turn 1"));
        Assert.Equal("res1", r1.ResponseText);
        Assert.Equal(1, routing.RouteCallCount);
        Assert.Equal(1, resumableCandidate.CreateSessionCount);

        // Turn 2: uses sticky binding, zero additional route calls
        resumableCandidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("res2", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        ModelExecutionResult r2 = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("turn 2"));
        Assert.Equal("res2", r2.ResponseText);
        Assert.Equal(1, routing.RouteCallCount);
    }

    [Fact]
    public async Task Session_UnderlyingCreateSessionFailure_Propagates_WrapperRemainsUnboundForRetry()
    {
        TestCandidateRuntime candidate = new();
        candidate.ThrowOnCreateSession = new InvalidOperationException("create failed");

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        AgentSessionReference session = runtime.CreateSession();

        // First attempt fails at CreateSession
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("test")));

        Assert.Equal("create failed", ex.Message);
        Assert.Equal(0, classifier.InvocationCount);

        // Later turn retries binding and succeeds
        candidate.ThrowOnCreateSession = null;
        candidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("success", new ModelExecutionTelemetry(null, TimeSpan.Zero)));

        ModelExecutionResult r2 = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("retry turn"));
        Assert.Equal("success", r2.ResponseText);
        Assert.Equal(2, routing.RouteCallCount);
    }

    [Fact]
    public async Task Session_BoundNonStreaming_RetriesSameSessionReference_NoFallback()
    {
        TestCandidateRuntime candidate1 = new();
        int attempts = 0;
        candidate1.ExecuteInSessionAsyncFunc = (sess, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("session timeout");
            }
            return Task.FromResult(new ModelExecutionResult($"turn ok for {sess}", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        };

        TestCandidateRuntime candidate2 = new();
        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        AgentSessionReference session = runtime.CreateSession();
        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("prompt"));

        Assert.Contains("turn ok", result.ResponseText);
        Assert.Equal(2, candidate1.ExecuteInSessionAsyncCount);
        Assert.Equal(0, candidate2.ExecuteInSessionAsyncCount);
        Assert.Equal(1, classifier.InvocationCount);
    }

    [Fact]
    public async Task Session_ExhaustedBoundTurn_PreservesBinding_SubsequentTurnCanSucceed()
    {
        TestCandidateRuntime candidate = new();
        candidate.ExecuteInSessionAsyncFunc = (_, _) => throw new HttpRequestException("transient failure");

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        AgentSessionReference session = runtime.CreateSession();

        // Turn 1 exhausts retries
        await Assert.ThrowsAsync<HttpRequestException>(
            () => runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("t1")));

        Assert.Equal(2, candidate.ExecuteInSessionAsyncCount);

        // Turn 2 can run on same session
        candidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("t2 ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        ModelExecutionResult r2 = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("t2"));

        Assert.Equal("t2 ok", r2.ResponseText);
        Assert.Equal(1, routing.RouteCallCount); // Sticky: no new routing
    }

    [Fact]
    public async Task Session_StreamingBound_RetryBeforeFirstUpdate_RetriesSameSession()
    {
        TestCandidateRuntime candidate = new();
        int attempts = 0;
        ModelExecutionUpdate successUpdate = new("session streamed update");

        candidate.ExecuteStreamingInSessionAsyncFunc = () =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("stream start error");
            }
            return CreateAsyncEnumerable([successUpdate]);
        };

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        AgentSessionReference session = runtime.CreateSession();
        List<ModelExecutionUpdate> received = [];

        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(session, new ModelExecutionRequest("prompt")))
        {
            received.Add(update);
        }

        Assert.Single(received);
        Assert.Same(successUpdate, received[0]);
        Assert.Equal(2, candidate.ExecuteStreamingInSessionAsyncCount);
        Assert.Equal(1, classifier.InvocationCount);
    }

    [Fact]
    public async Task Session_StreamingPartialOutputFailure_KeepsBinding_AllowsSubsequentTurn()
    {
        ModelExecutionUpdate partial = new("partial text");
        TestCandidateRuntime candidate = new();
        candidate.ExecuteStreamingInSessionAsyncFunc = () => CreateAsyncEnumerableWithFailure([partial], new IOException("stream broken"));

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        AgentSessionReference session = runtime.CreateSession();

        // Streaming turn fails after partial output
        List<ModelExecutionUpdate> received = [];
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(session, new ModelExecutionRequest("test")))
            {
                received.Add(update);
            }
        });

        Assert.Single(received);
        Assert.Same(partial, received[0]);

        // Subsequent turn can succeed on same binding
        candidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("subsequent turn ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        ModelExecutionResult r2 = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("turn 2"));

        Assert.Equal("subsequent turn ok", r2.ResponseText);
        Assert.Equal(1, routing.RouteCallCount);
    }

    [Fact]
    public async Task Session_ConcurrentTurns_RejectedWithInvalidOperationException()
    {
        TaskCompletionSource<ModelExecutionResult> tcs = new();
        TestCandidateRuntime candidate = new();
        candidate.ExecuteInSessionAsyncFunc = (_, _) => tcs.Task;

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();

        // Start active turn
        Task<ModelExecutionResult> turn1 = runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("turn 1"));

        // Concurrent non-streaming turn
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("turn 2")));

        // Concurrent streaming turn
        IAsyncEnumerable<ModelExecutionUpdate> concurrentStream =
            runtime.ExecuteStreamingInSessionAsync(session, new ModelExecutionRequest("turn 3"));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in concurrentStream)
            {
            }
        });

        // Release first turn
        tcs.SetResult(new ModelExecutionResult("t1 complete", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        ModelExecutionResult res1 = await turn1;
        Assert.Equal("t1 complete", res1.ResponseText);

        // Turn gate released: subsequent turn succeeds
        candidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("t4 complete", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        ModelExecutionResult res4 = await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("turn 4"));
        Assert.Equal("t4 complete", res4.ResponseText);
    }

    [Fact]
    public async Task EndSessionAsync_UnboundSession_ReturnsTrue_NoRouteOrCandidateCalls()
    {
        TestRoutingRuntime routing = new();
        TestCandidateRuntime candidate = new();

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();

        bool ended = await runtime.EndSessionAsync(session);
        Assert.True(ended);
        Assert.Equal(0, routing.RouteCallCount);
        Assert.Equal(0, candidate.EndSessionAsyncCount);

        // Repeated EndSession on already-ended session returns false
        bool endedAgain = await runtime.EndSessionAsync(session);
        Assert.False(endedAgain);
    }

    [Fact]
    public async Task EndSessionAsync_BoundSession_DelegatesOnce_RemovesSessionOnBothTrueAndFalse()
    {
        TestCandidateRuntime candidate = new();
        candidate.EndSessionResult = true;

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();
        // Bind via execution
        candidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("bind"));

        bool ended = await runtime.EndSessionAsync(session);
        Assert.True(ended);
        Assert.Equal(1, candidate.EndSessionAsyncCount);

        // Next call returns false (already removed)
        bool endedAgain = await runtime.EndSessionAsync(session);
        Assert.False(endedAgain);
        Assert.Equal(1, candidate.EndSessionAsyncCount);
    }

    [Fact]
    public async Task EndSessionAsync_DuringActiveTurn_WaitsForTurnToComplete()
    {
        TaskCompletionSource<ModelExecutionResult> turnTcs = new();
        TestCandidateRuntime candidate = new();
        candidate.ExecuteInSessionAsyncFunc = (_, _) => turnTcs.Task;
        candidate.EndSessionResult = true;

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();
        Task<ModelExecutionResult> turnTask = runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("long turn"));

        Task<bool> endTask = runtime.EndSessionAsync(session);

        // While turn is running, EndSession has NOT delegated to candidate yet
        Assert.Equal(0, candidate.EndSessionAsyncCount);

        // Complete the turn
        turnTcs.SetResult(new ModelExecutionResult("done", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        await turnTask;

        // EndSession completes
        bool endResult = await endTask;
        Assert.True(endResult);
        Assert.Equal(1, candidate.EndSessionAsyncCount);
    }

    [Fact]
    public async Task EndSessionAsync_HonorsCancellationWhileWaiting_LeavesSessionIntact()
    {
        TaskCompletionSource<ModelExecutionResult> turnTcs = new();
        TestCandidateRuntime candidate = new();
        candidate.ExecuteInSessionAsyncFunc = (_, _) => turnTcs.Task;

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();
        Task<ModelExecutionResult> turnTask = runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("long turn"));

        using CancellationTokenSource cts = new();
        Task<bool> endTask = runtime.EndSessionAsync(session, cts.Token);

        // Cancel while waiting
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => endTask);

        // Turn completes normally
        turnTcs.SetResult(new ModelExecutionResult("done", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        await turnTask;

        // Session was preserved; explicit later EndSession succeeds
        candidate.EndSessionResult = true;
        bool laterEndResult = await runtime.EndSessionAsync(session);
        Assert.True(laterEndResult);
        Assert.Equal(1, candidate.EndSessionAsyncCount);
    }

    [Fact]
    public async Task EndSessionAsync_ConcurrentCalls_DoNotDoubleEnd()
    {
        TaskCompletionSource<bool> endTcs = new();
        TestCandidateRuntime candidate = new();
        candidate.EndSessionAsyncFunc = () => endTcs.Task;

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            new TestClassifier());

        AgentSessionReference session = runtime.CreateSession();
        candidate.ExecuteInSessionAsyncFunc = (_, _) => Task.FromResult(new ModelExecutionResult("ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        await runtime.ExecuteInSessionAsync(session, new ModelExecutionRequest("bind"));

        Task<bool> end1 = runtime.EndSessionAsync(session);
        Task<bool> end2 = runtime.EndSessionAsync(session);

        // Release underlying end
        endTcs.SetResult(true);

        bool r1 = await end1;
        bool r2 = await end2;

        Assert.True(r1);
        Assert.False(r2); // Second observer sees session already ended
        Assert.Equal(1, candidate.EndSessionAsyncCount);
    }

    [Fact]
    public async Task Telemetry_RetrySuccess_ExposesOnlySuccessfulResultTelemetry()
    {
        ModelExecutionTelemetry tSuccess = new(new ModelTokenUsage(20, 30), TimeSpan.FromMilliseconds(200), 0.10m, "USD");
        ModelExecutionResult successResult = new("success after retry", tSuccess);

        TestCandidateRuntime candidate = new();
        int attempts = 0;
        candidate.ExecuteAsyncFunc = _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new TimeoutException("fail 1");
            }
            return Task.FromResult(successResult);
        };

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([CreateCandidate("m1", candidate)]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(2, TimeSpan.Zero),
            classifier);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("test"));

        Assert.Same(successResult, result);
        Assert.NotNull(result.Telemetry);
        Assert.Same(tSuccess, result.Telemetry);
        Assert.NotNull(result.Telemetry.TokenUsage);
        Assert.Equal(20, result.Telemetry.TokenUsage.InputTokenCount);
        Assert.Equal(30, result.Telemetry.TokenUsage.OutputTokenCount);
    }

    [Fact]
    public async Task Telemetry_FallbackSuccess_ExposesOnlyFallbackResultTelemetry()
    {
        TestCandidateRuntime candidate1 = new();
        candidate1.ExecuteAsyncFunc = _ => throw new HttpRequestException("c1 fail");

        ModelExecutionTelemetry tFallback = new(new ModelTokenUsage(15, 25), TimeSpan.FromMilliseconds(150), 0.08m, "USD");
        ModelExecutionResult fallbackResult = new("fallback success", tFallback);

        TestCandidateRuntime candidate2 = new();
        candidate2.ExecuteAsyncFunc = _ => Task.FromResult(fallbackResult);

        TestRoutingRuntime routing = new();
        routing.RouteAsyncFunc = (_, _) => Task.FromResult<IReadOnlyList<ModelRouteCandidate>>(
        [
            CreateCandidate("m1", candidate1),
            CreateCandidate("m2", candidate2)
        ]);

        TestClassifier classifier = new() { IsRetryableResult = true };

        ResilientModelExecutionRuntime runtime = new(
            routing,
            DefaultCapabilities,
            new ModelRuntimeResiliencePolicy(1, TimeSpan.Zero),
            classifier);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("test"));

        Assert.Same(fallbackResult, result);
        Assert.Same(tFallback, result.Telemetry);
    }

    private static async IAsyncEnumerable<ModelExecutionUpdate> CreateAsyncEnumerable(
        IEnumerable<ModelExecutionUpdate> items_)
    {
        foreach (ModelExecutionUpdate item in items_)
        {
            yield return item;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ModelExecutionUpdate> CreateAsyncEnumerableWithFailure(
        IEnumerable<ModelExecutionUpdate> items_,
        Exception failure_)
    {
        foreach (ModelExecutionUpdate item in items_)
        {
            yield return item;
        }
        await Task.CompletedTask;
        throw failure_;
    }

    private sealed class CustomDisposableAsyncEnumerable : IAsyncEnumerable<ModelExecutionUpdate>
    {
        private readonly IEnumerable<ModelExecutionUpdate> _items;
        private readonly Exception? _disposalFailure;

        public CustomDisposableAsyncEnumerable(
            IEnumerable<ModelExecutionUpdate> items_,
            Exception? disposalFailure_)
        {
            this._items = items_;
            this._disposalFailure = disposalFailure_;
        }

        public IAsyncEnumerator<ModelExecutionUpdate> GetAsyncEnumerator(CancellationToken cancellationToken_ = default)
        {
            return new CustomDisposableEnumerator(this._items.GetEnumerator(), this._disposalFailure);
        }

        private sealed class CustomDisposableEnumerator : IAsyncEnumerator<ModelExecutionUpdate>
        {
            private readonly IEnumerator<ModelExecutionUpdate> _enumerator;
            private readonly Exception? _disposalFailure;

            public CustomDisposableEnumerator(
                IEnumerator<ModelExecutionUpdate> enumerator_,
                Exception? disposalFailure_)
            {
                this._enumerator = enumerator_;
                this._disposalFailure = disposalFailure_;
            }

            public ModelExecutionUpdate Current => this._enumerator.Current;

            public ValueTask<bool> MoveNextAsync()
            {
                return ValueTask.FromResult(this._enumerator.MoveNext());
            }

            public ValueTask DisposeAsync()
            {
                this._enumerator.Dispose();
                if (this._disposalFailure is not null)
                {
                    throw this._disposalFailure;
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ScriptedStreamingAsyncEnumerable : IAsyncEnumerable<ModelExecutionUpdate>
    {
        private readonly Func<IAsyncEnumerator<ModelExecutionUpdate>> _factory;

        public ScriptedStreamingAsyncEnumerable(Func<IAsyncEnumerator<ModelExecutionUpdate>> factory_)
        {
            this._factory = factory_;
        }

        public IAsyncEnumerator<ModelExecutionUpdate> GetAsyncEnumerator(CancellationToken cancellationToken_ = default)
        {
            return this._factory();
        }
    }

    private sealed class ScriptedStreamingEnumerator : IAsyncEnumerator<ModelExecutionUpdate>
    {
        private readonly Func<ValueTask<bool>> _moveNextFunc;
        private readonly Func<ModelExecutionUpdate> _currentFunc;
        private readonly Func<ValueTask>? _disposeFunc;

        public ScriptedStreamingEnumerator(
            Func<ValueTask<bool>> moveNextFunc_,
            Func<ModelExecutionUpdate> currentFunc_,
            Func<ValueTask>? disposeFunc_ = null)
        {
            this._moveNextFunc = moveNextFunc_;
            this._currentFunc = currentFunc_;
            this._disposeFunc = disposeFunc_;
        }

        public ModelExecutionUpdate Current => this._currentFunc();

        public ValueTask<bool> MoveNextAsync() => this._moveNextFunc();

        public ValueTask DisposeAsync()
        {
            if (this._disposeFunc is not null)
            {
                return this._disposeFunc();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestRoutingRuntime : IModelRoutingRuntime
    {
        public int RouteCallCount { get; private set; }
        public AgentCapabilitySet? LastRequiredCapabilities { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public Func<AgentCapabilitySet, CancellationToken, Task<IReadOnlyList<ModelRouteCandidate>>>? RouteAsyncFunc { get; set; }

        public Task<IReadOnlyList<ModelRouteCandidate>> RouteAsync(
            AgentCapabilitySet requiredCapabilities_,
            CancellationToken cancellationToken_ = default)
        {
            this.RouteCallCount++;
            this.LastRequiredCapabilities = requiredCapabilities_;
            this.LastCancellationToken = cancellationToken_;

            if (this.RouteAsyncFunc is not null)
            {
                return this.RouteAsyncFunc(requiredCapabilities_, cancellationToken_);
            }

            return Task.FromResult<IReadOnlyList<ModelRouteCandidate>>([]);
        }
    }

    private sealed class TestClassifier : IModelExecutionFailureClassifier
    {
        public int InvocationCount { get; private set; }
        public ModelRouteCandidate? LastCandidate { get; private set; }
        public Exception? LastException { get; private set; }
        public bool IsRetryableResult { get; set; } = true;
        public Exception? ThrowOnIsRetryable { get; set; }

        public bool IsRetryable(ModelRouteCandidate candidate_, Exception exception_)
        {
            this.InvocationCount++;
            this.LastCandidate = candidate_;
            this.LastException = exception_;

            if (this.ThrowOnIsRetryable is not null)
            {
                throw this.ThrowOnIsRetryable;
            }

            return this.IsRetryableResult;
        }
    }

    private sealed class PlainExecutionOnlyRuntime : IModelExecutionRuntime
    {
        public int ExecuteAsyncCount { get; private set; }

        public Task<ModelExecutionResult> ExecuteAsync(
            ModelExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.ExecuteAsyncCount++;
            return Task.FromResult(new ModelExecutionResult("plain ok", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        }
    }

    private sealed class TestCandidateRuntime : IResumableModelSessionRuntime
    {
        public int ExecuteAsyncCount { get; private set; }
        public ModelExecutionRequest? LastExecuteRequest { get; private set; }
        public CancellationToken LastExecuteToken { get; private set; }
        public Func<ModelExecutionRequest, Task<ModelExecutionResult>>? ExecuteAsyncFunc { get; set; }

        public int ExecuteStreamingAsyncCount { get; private set; }
        public List<ModelExecutionUpdate> StreamingUpdates { get; set; } = [];
        public Func<IAsyncEnumerable<ModelExecutionUpdate>>? ExecuteStreamingAsyncFunc { get; set; }

        public int CreateSessionCount { get; private set; }
        public Exception? ThrowOnCreateSession { get; set; }

        public int ExecuteInSessionAsyncCount { get; private set; }
        public Func<AgentSessionReference, ModelExecutionRequest, Task<ModelExecutionResult>>? ExecuteInSessionAsyncFunc { get; set; }

        public int ExecuteStreamingInSessionAsyncCount { get; private set; }
        public Func<IAsyncEnumerable<ModelExecutionUpdate>>? ExecuteStreamingInSessionAsyncFunc { get; set; }

        public int EndSessionAsyncCount { get; private set; }
        public bool EndSessionResult { get; set; } = true;
        public Func<Task<bool>>? EndSessionAsyncFunc { get; set; }

        public Task<ModelExecutionResult> ExecuteAsync(
            ModelExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.ExecuteAsyncCount++;
            this.LastExecuteRequest = request_;
            this.LastExecuteToken = cancellationToken_;

            if (this.ExecuteAsyncFunc is not null)
            {
                return this.ExecuteAsyncFunc(request_);
            }

            return Task.FromResult(new ModelExecutionResult("default candidate", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        }

        public IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingAsync(
            ModelExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.ExecuteStreamingAsyncCount++;
            if (this.ExecuteStreamingAsyncFunc is not null)
            {
                return this.ExecuteStreamingAsyncFunc();
            }

            return CreateAsyncEnumerable(this.StreamingUpdates);
        }

        public AgentSessionReference CreateSession()
        {
            this.CreateSessionCount++;
            if (this.ThrowOnCreateSession is not null)
            {
                throw this.ThrowOnCreateSession;
            }

            return new AgentSessionReference($"cand-session-{Guid.NewGuid():N}");
        }

        public Task<ModelExecutionResult> ExecuteInSessionAsync(
            AgentSessionReference sessionReference_,
            ModelExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.ExecuteInSessionAsyncCount++;
            if (this.ExecuteInSessionAsyncFunc is not null)
            {
                return this.ExecuteInSessionAsyncFunc(sessionReference_, request_);
            }

            return Task.FromResult(new ModelExecutionResult("default in session", new ModelExecutionTelemetry(null, TimeSpan.Zero)));
        }

        public IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionAsync(
            AgentSessionReference sessionReference_,
            ModelExecutionRequest request_,
            CancellationToken cancellationToken_ = default)
        {
            this.ExecuteStreamingInSessionAsyncCount++;
            if (this.ExecuteStreamingInSessionAsyncFunc is not null)
            {
                return this.ExecuteStreamingInSessionAsyncFunc();
            }

            return CreateAsyncEnumerable(this.StreamingUpdates);
        }

        public Task<bool> EndSessionAsync(
            AgentSessionReference sessionReference_,
            CancellationToken cancellationToken_ = default)
        {
            this.EndSessionAsyncCount++;
            if (this.EndSessionAsyncFunc is not null)
            {
                return this.EndSessionAsyncFunc();
            }

            return Task.FromResult(this.EndSessionResult);
        }
    }
}
