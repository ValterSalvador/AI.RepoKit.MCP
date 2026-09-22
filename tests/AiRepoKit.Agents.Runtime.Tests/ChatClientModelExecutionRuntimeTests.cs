namespace AiRepoKit.Agents.Runtime.Tests;

using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Microsoft.Extensions.AI;
using Xunit;

public sealed class ChatClientModelExecutionRuntimeTests
{
    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ChatClientModelExecutionRuntime(null!));
    }

    [Fact]
    public async Task ExecuteAsync_NullRequest_ThrowsArgumentNullException()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runtime.ExecuteAsync(null!));
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceledToken_ThrowsOperationCanceledException_WithoutCallingClient()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        ModelExecutionRequest request = new("Hello");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(request, cts.Token));

        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_SendsExactlyOneUserMessage_WithExactPromptText()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        string prompt = "Explain quantum computing in one sentence.";
        ModelExecutionRequest request = new(prompt);

        await runtime.ExecuteAsync(request);

        Assert.Equal(1, fakeClient.GetResponseCallCount);
        Assert.NotNull(fakeClient.LastMessages);
        Assert.Single(fakeClient.LastMessages);

        ChatMessage sentMessage = fakeClient.LastMessages[0];
        Assert.Equal(ChatRole.User, sentMessage.Role);
        Assert.Equal(prompt, sentMessage.Text);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotAddExtraSystemOrToolMessages()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionRequest request = new("Just a prompt");

        await runtime.ExecuteAsync(request);

        Assert.NotNull(fakeClient.LastMessages);
        Assert.All(fakeClient.LastMessages, m => Assert.Equal(ChatRole.User, m.Role));
    }

    [Fact]
    public async Task ExecuteAsync_WithoutStructuredOutput_SetsNoResponseFormat()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionRequest request = new("No schema prompt", structuredOutput_: null);

        await runtime.ExecuteAsync(request);

        Assert.Null(fakeClient.LastOptions);
    }

    [Fact]
    public async Task ExecuteAsync_WithStructuredOutput_MapsJsonSchemaToChatOptionsResponseFormat()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        string jsonSchema = "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}";
        StructuredOutputContract contract = new(jsonSchema);
        ModelExecutionRequest request = new("Generate person", contract);

        await runtime.ExecuteAsync(request);

        Assert.NotNull(fakeClient.LastOptions);
        Assert.NotNull(fakeClient.LastOptions.ResponseFormat);

        // Verify the response format holds the equivalent JSON schema
        ChatResponseFormat format = fakeClient.LastOptions.ResponseFormat;
        Assert.IsAssignableFrom<ChatResponseFormatJson>(format);

        ChatResponseFormatJson jsonFormat = (ChatResponseFormatJson)format;
        Assert.NotNull(jsonFormat.Schema);

        using JsonDocument parsedExpected = JsonDocument.Parse(jsonSchema);
        string actualSchemaText = jsonFormat.Schema.Value.GetRawText();
        using JsonDocument parsedActual = JsonDocument.Parse(actualSchemaText);

        Assert.True(
            JsonElement.DeepEquals(parsedExpected.RootElement, parsedActual.RootElement),
            "Mapped JSON schema must be structurally equivalent to input JSON schema.");
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotOverrideModelId()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionRequest request = new("Test prompt");

        await runtime.ExecuteAsync(request);

        if (fakeClient.LastOptions is not null)
        {
            Assert.Null(fakeClient.LastOptions.ModelId);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PassesThroughExactResponseText()
    {
        string expectedText = "The model produced this exact string \r\n with special <characters>.";
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, expectedText))
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.Equal(expectedText, result.ResponseText);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyResponse_ReturnsEmptyString()
    {
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse([])
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.Equal(string.Empty, result.ResponseText);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesCancellationToken()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        await runtime.ExecuteAsync(new ModelExecutionRequest("Test"), token);

        Assert.Equal(token, fakeClient.LastCancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_ModelThrowsOperationCanceledException_PropagatesDirectly()
    {
        FakeChatClient fakeClient = new()
        {
            ExceptionToThrow = new OperationCanceledException("Model call canceled.")
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
    }

    [Fact]
    public async Task ExecuteAsync_ModelThrowsNonCancellationException_PropagatesUnchanged()
    {
        InvalidOperationException expectedException = new("Provider endpoint failed.");
        FakeChatClient fakeClient = new()
        {
            ExceptionToThrow = expectedException
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));

        Assert.Same(expectedException, thrown);
    }

    [Fact]
    public void Runtime_DoesNotOwnOrDisposeInjectedClient()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        // runtime does not implement IDisposable
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(ChatClientModelExecutionRuntime)));
        Assert.False(fakeClient.Disposed);
    }

    [Fact]
    public async Task ExecuteAsync_ConsecutiveCalls_ShareNoHistory_AndSendOnlyCurrentUserMessage()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result1 = await runtime.ExecuteAsync(new ModelExecutionRequest("First prompt"));
        ModelExecutionResult result2 = await runtime.ExecuteAsync(new ModelExecutionRequest("Second prompt"));

        Assert.Equal(2, fakeClient.GetResponseCallCount);
        Assert.Equal(2, fakeClient.InvocationsMessages.Count);

        IReadOnlyList<ChatMessage> firstCallMessages = fakeClient.InvocationsMessages[0];
        Assert.Single(firstCallMessages);
        Assert.Equal("First prompt", firstCallMessages[0].Text);

        IReadOnlyList<ChatMessage> secondCallMessages = fakeClient.InvocationsMessages[1];
        Assert.Single(secondCallMessages);
        Assert.Equal("Second prompt", secondCallMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotSetConversationId_EvenIfClientResponseProvidedOne()
    {
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Response"))
            {
                ConversationId = "provider-conv-123"
            }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await runtime.ExecuteAsync(new ModelExecutionRequest("First prompt"));
        await runtime.ExecuteAsync(new ModelExecutionRequest("Second prompt"));

        Assert.Equal(2, fakeClient.GetResponseCallCount);
        foreach (ChatOptions? options in fakeClient.InvocationsOptions)
        {
            if (options is not null)
            {
                Assert.Null(options.ConversationId);
            }
        }
    }

    // =========================================================================
    // Section 50: One-shot timeout tests
    // =========================================================================

    [Fact]
    public async Task ExecuteAsync_NullTimeout_PreservesP01Behavior()
    {
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "P01 preserved"))
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionRequest request = new("Hello", structuredOutput_: null, timeout_: null);
        ModelExecutionResult result = await runtime.ExecuteAsync(request);

        Assert.Equal("P01 preserved", result.ResponseText);
        Assert.Equal(1, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PreCanceledCallerToken_CausesZeroProviderCalls()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        ModelExecutionRequest request = new("Hello", structuredOutput_: null, timeout_: TimeSpan.FromSeconds(10));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(request, cts.Token));

        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_TimeoutExpires_ThrowsTimeoutException()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionRequest request = new("Hello", timeout_: TimeSpan.FromSeconds(5));

        Task<ModelExecutionResult> executeTask = runtime.ExecuteAsync(request);
        await callStarted.Task;

        timeProvider.Advance(TimeSpan.FromSeconds(6));

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => executeTask);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellation_ThrowsOperationCanceledException()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        ModelExecutionRequest request = new("Hello", timeout_: TimeSpan.FromMinutes(1));

        Task<ModelExecutionResult> executeTask = runtime.ExecuteAsync(request, cts.Token);
        await callStarted.Task;

        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => executeTask);
    }

    [Fact]
    public async Task ExecuteAsync_CallerCancellationAndTimeoutRace_CallerCancellationWinsPrecedence()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        ModelExecutionRequest request = new("Hello", timeout_: TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteAsync(request, cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_ProviderExceptionBeforeTimeout_PropagatesUnchanged()
    {
        InvalidOperationException providerEx = new("Provider connection error");
        FakeChatClient fakeClient = new()
        {
            ExceptionToThrow = providerEx
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionRequest request = new("Hello", timeout_: TimeSpan.FromSeconds(30));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(request));

        Assert.Same(providerEx, thrown);
    }

    [Fact]
    public async Task ExecuteAsync_NonCooperativeProvider_StillRespectsCallerVisibleRuntimeTimeout()
    {
        FakeChatClient fakeClient = new()
        {
            NonCooperativeGetResponse = true
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionRequest request = new("Hello", timeout_: TimeSpan.FromSeconds(2));

        Task<ModelExecutionResult> executeTask = runtime.ExecuteAsync(request);

        timeProvider.Advance(TimeSpan.FromSeconds(3));

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => executeTask);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_Timeout_DoesNotDisposeIChatClient()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionRequest request = new("Hello", timeout_: TimeSpan.FromSeconds(1));
        Task<ModelExecutionResult> executeTask = runtime.ExecuteAsync(request);
        await callStarted.Task;

        timeProvider.Advance(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutException>(() => executeTask);
        Assert.False(fakeClient.Disposed);
    }

    // =========================================================================
    // Section 52: One-shot streaming tests
    // =========================================================================

    [Fact]
    public async Task ExecuteStreamingAsync_InvokesGetStreamingResponseAsyncExactlyOnce_AndGetResponseAsyncZeroTimes()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Hello"),
                new ChatResponseUpdate(ChatRole.Assistant, " World")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionRequest request = new("Stream test prompt");
        List<ModelExecutionUpdate> updates = [];

        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(request))
        {
            updates.Add(update);
        }

        Assert.Equal(1, fakeClient.GetStreamingResponseCallCount);
        Assert.Equal(0, fakeClient.GetResponseCallCount);
        Assert.Equal(3, updates.Count);
        Assert.Equal("Hello", updates[0].ResponseTextDelta);
        Assert.Null(updates[0].Telemetry);
        Assert.Equal(" World", updates[1].ResponseTextDelta);
        Assert.Null(updates[1].Telemetry);
        Assert.Equal(string.Empty, updates[2].ResponseTextDelta);
        Assert.NotNull(updates[2].Telemetry);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_SendsExactlyOneUserMessage_WithExactPromptText()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "done")]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        string prompt = "Explain relativity in \r\n 5 words \t exactly.";
        ModelExecutionRequest request = new(prompt);

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(request))
        {
        }

        Assert.NotNull(fakeClient.LastStreamingMessages);
        Assert.Single(fakeClient.LastStreamingMessages);
        Assert.Equal(ChatRole.User, fakeClient.LastStreamingMessages[0].Role);
        Assert.Equal(prompt, fakeClient.LastStreamingMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_PreservesOrderedUpdates()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "chunk1"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk2"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk3")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Equal(["chunk1", "chunk2", "chunk3"], updates.Where(u => u.Telemetry is null).Select(u => u.ResponseTextDelta).ToArray());
        Assert.Equal(string.Empty, updates.Last().ResponseTextDelta);
        Assert.NotNull(updates.Last().Telemetry);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_YieldsOneRuntimeUpdatePerProviderUpdate()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "a"),
                new ChatResponseUpdate(ChatRole.Assistant, "b"),
                new ChatResponseUpdate(ChatRole.Assistant, "c"),
                new ChatResponseUpdate(ChatRole.Assistant, "d")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Equal(4, updates.Count(u => u.Telemetry is null));
        Assert.Single(updates, u => u.Telemetry is not null);
        Assert.Equal(5, updates.Count);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_PreservesEmptyUpdate()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "start"),
                new ChatResponseUpdate(ChatRole.Assistant, string.Empty),
                new ChatResponseUpdate(ChatRole.Assistant, "end")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Equal(["start", string.Empty, "end"], updates.Where(u => u.Telemetry is null).Select(u => u.ResponseTextDelta).ToArray());
        Assert.Equal(string.Empty, updates.Last().ResponseTextDelta);
        Assert.NotNull(updates.Last().Telemetry);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_NoStateRetainedBetweenOneShotStreams()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "res")]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Stream 1")))
        {
        }

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Stream 2")))
        {
        }

        Assert.Equal(2, fakeClient.GetStreamingResponseCallCount);
        Assert.Single(fakeClient.InvocationsStreamingMessages[0]);
        Assert.Equal("Stream 1", fakeClient.InvocationsStreamingMessages[0][0].Text);

        Assert.Single(fakeClient.InvocationsStreamingMessages[1]);
        Assert.Equal("Stream 2", fakeClient.InvocationsStreamingMessages[1][0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_StructuredOutputCorrectlyMapped()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "{}")]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        string jsonSchema = "{\"type\":\"object\",\"properties\":{\"count\":{\"type\":\"integer\"}}}";
        StructuredOutputContract contract = new(jsonSchema);
        ModelExecutionRequest request = new("Stream with schema", contract);

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(request))
        {
        }

        Assert.NotNull(fakeClient.LastStreamingOptions?.ResponseFormat);
        ChatResponseFormatJson jsonFormat = Assert.IsAssignableFrom<ChatResponseFormatJson>(
            fakeClient.LastStreamingOptions.ResponseFormat);
        Assert.NotNull(jsonFormat.Schema);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_NullRequest_ThrowsArgumentNullException()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        Assert.Throws<ArgumentNullException>(
            () => runtime.ExecuteStreamingAsync(null!));
    }

    [Fact]
    public async Task ExecuteStreamingAsync_PreCanceledCallerToken_ThrowsOperationCanceledException_BeforeProviderCall()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        ModelExecutionRequest request = new("Pre-canceled");

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(request, cts.Token))
            {
            }
        });

        Assert.Equal(0, fakeClient.GetStreamingResponseCallCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ProviderThrowsException_PropagatesUnchanged()
    {
        InvalidOperationException providerEx = new("Stream failed at source");
        FakeChatClient fakeClient = new()
        {
            StreamingExceptionToThrow = providerEx
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
            {
            }
        });

        Assert.Same(providerEx, thrown);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_CallerCancellationDuringStream_ThrowsOperationCanceledException()
    {
        TaskCompletionSource proceedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateCancellableStream(proceedTcs));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource cts = new();
        ModelExecutionRequest request = new("Cancel stream test", timeout_: TimeSpan.FromMinutes(1));

        List<string> deltas = [];
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(request, cts.Token))
            {
                deltas.Add(update.ResponseTextDelta);
                cts.Cancel(); // Cancel after first update
                proceedTcs.TrySetResult();
            }
        });

        Assert.Single(deltas);
        Assert.Equal("first", deltas[0]);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_TimeoutExpiresWhileConsumerPauses_ThrowsTimeoutExceptionOnNextMoveNext()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "chunk1"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk2")
            ]
        };

        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        ModelExecutionRequest request = new("Pause timeout test", timeout_: TimeSpan.FromSeconds(5));

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(request).GetAsyncEnumerator();

        // First update consumed
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("chunk1", enumerator.Current.ResponseTextDelta);

        // Consumer pauses and timeout expires during pause
        timeProvider.Advance(TimeSpan.FromSeconds(6));

        // Next MoveNextAsync must classify expired Runtime timeout
        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await enumerator.MoveNextAsync();
        });

        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_TimeoutExpiresWhileMoveNextPending_ThrowsTimeoutException()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new();
        TaskCompletionSource chunk1Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource chunk2Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.EnqueueStreamingSequence(() => CreateDelayedStream(chunk1Proceed, chunk2Proceed));

        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        ModelExecutionRequest request = new("Pending MoveNext timeout test", timeout_: TimeSpan.FromSeconds(5));

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(request).GetAsyncEnumerator();

        chunk1Proceed.SetResult();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("chunk1", enumerator.Current.ResponseTextDelta);

        // Start pending MoveNext for chunk2
        ValueTask<bool> pendingMoveNext = enumerator.MoveNextAsync();
        Assert.False(pendingMoveNext.IsCompleted);

        // Advance time past timeout while pending
        timeProvider.Advance(TimeSpan.FromSeconds(6));

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(async () => await pendingMoveNext);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_NonCooperativeProvider_StillRespectsCallerVisibleRuntimeTimeout()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new()
        {
            NonCooperativeMoveNext = true
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionRequest request = new("Non-cooperative stream test", timeout_: TimeSpan.FromSeconds(3));

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(request).GetAsyncEnumerator();

        ValueTask<bool> pendingMoveNext = enumerator.MoveNextAsync();
        Assert.False(pendingMoveNext.IsCompleted);

        timeProvider.Advance(TimeSpan.FromSeconds(4));

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(async () => await pendingMoveNext);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateCancellableStream(
        TaskCompletionSource proceedTcs)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "first");
        await proceedTcs.Task;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "second");
    }

    [Fact]
    public async Task ExecuteAsync_ProviderThrowsTimeoutException_PropagatesUnchanged()
    {
        TimeoutException providerTimeout = new("Provider custom timeout failure");
        FakeChatClient fakeClient = new()
        {
            ExceptionToThrow = providerTimeout
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionRequest request = new("Prompt", timeout_: TimeSpan.FromSeconds(10));

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => runtime.ExecuteAsync(request));

        Assert.Same(providerTimeout, thrown);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ProviderThrowsTimeoutException_PropagatesUnchanged()
    {
        TimeoutException providerTimeout = new("Streaming provider timeout");
        FakeChatClient fakeClient = new()
        {
            StreamingExceptionToThrow = providerTimeout
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionRequest request = new("Prompt", timeout_: TimeSpan.FromSeconds(10));

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(request))
            {
            }
        });

        Assert.Same(providerTimeout, thrown);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ProviderDisposeThrows_PropagatesDisposalException()
    {
        InvalidOperationException disposalEx = new("Disposal failed at provider");
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => new ThrowingDisposeAsyncEnumerable<ChatResponseUpdate>(
            [new ChatResponseUpdate(ChatRole.Assistant, "hello")],
            disposalEx));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        ModelExecutionRequest request = new("Test");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(request))
            {
            }
        });

        Assert.Same(disposalEx, thrown);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_EarlyConsumerBreak_ProviderDisposeThrows_PropagatesDisposalException()
    {
        InvalidOperationException disposalEx = new("Disposal failed at provider on early break");
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => new ThrowingDisposeAsyncEnumerable<ChatResponseUpdate>(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "chunk1"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk2")
            ],
            disposalEx));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        ModelExecutionRequest request = new("Test early break");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(request))
            {
                break;
            }
        });

        Assert.Same(disposalEx, thrown);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_TimeoutReadyToClassify_CallerCanceledBeforeClassification_CallerCancellationWins()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new()
        {
            NonCooperativeMoveNext = true
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        using CancellationTokenSource callerCts = new();
        ModelExecutionRequest request = new("Prompt", timeout_: TimeSpan.FromSeconds(5));

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(request, callerCts.Token).GetAsyncEnumerator();

        ValueTask<bool> moveNext = enumerator.MoveNextAsync();
        Assert.False(moveNext.IsCompleted);

        // Advance time so timeout expires
        timeProvider.Advance(TimeSpan.FromSeconds(6));
        // Simultaneously cancel caller token before classification resumes
        callerCts.Cancel();

        OperationCanceledException ex = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await moveNext);

        Assert.Equal(callerCts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_WithoutTimeout_NonCooperativeProvider_CallerCancellationTerminatesWithOperationCanceledException()
    {
        FakeChatClient fakeClient = new()
        {
            NonCooperativeMoveNext = true
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        using CancellationTokenSource callerCts = new();
        ModelExecutionRequest request = new("No timeout non-cooperative");

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(request, callerCts.Token).GetAsyncEnumerator();

        ValueTask<bool> moveNext = enumerator.MoveNextAsync();
        Assert.False(moveNext.IsCompleted);

        callerCts.Cancel();

        OperationCanceledException ex = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await moveNext);

        Assert.Equal(callerCts.Token, ex.CancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateDelayedStream(
        TaskCompletionSource chunk1Proceed,
        TaskCompletionSource chunk2Proceed)
    {
        await chunk1Proceed.Task;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk1");
        await chunk2Proceed.Task;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk2");
    }

    // =========================================================================
    // Section 54: V5.P06 ExecuteAsync Telemetry & Latency Tests
    // =========================================================================

    [Fact]
    public void Constructor_PricingConstructor_NullClient_ThrowsArgumentNullException()
    {
        ModelTokenPricing pricing = new("USD", 1m, 2m);
        Assert.Throws<ArgumentNullException>(
            () => new ChatClientModelExecutionRuntime(null!, pricing));
    }

    [Fact]
    public void Constructor_PricingConstructor_NullPricing_ThrowsArgumentNullException()
    {
        FakeChatClient fakeClient = new();
        Assert.Throws<ArgumentNullException>(
            () => new ChatClientModelExecutionRuntime(fakeClient, (ModelTokenPricing)null!));
    }

    [Fact]
    public void Constructor_OneArgumentConstructor_ConfiguresNullPricing()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        Assert.NotNull(runtime);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulCall_AlwaysReturnsNonNullTelemetry()
    {
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
    }

    [Fact]
    public async Task ExecuteAsync_ResponseWithoutUsage_TelemetryHasNullUsageAndNullCostAndNullCurrency()
    {
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
            {
                Usage = null
            }
        };
        ModelTokenPricing pricing = new("USD", 1m, 2m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Null(result.Telemetry.TokenUsage);
        Assert.Null(result.Telemetry.EstimatedCost);
        Assert.Null(result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_UsageDetails_MapsAllFiveNormalizedTokenFieldsExactly()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 100,
            OutputTokenCount = 50,
            TotalTokenCount = 150,
            CachedInputTokenCount = 20,
            ReasoningTokenCount = 10
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
            {
                Usage = usage
            }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry?.TokenUsage);
        Assert.Equal(100, result.Telemetry.TokenUsage.InputTokenCount);
        Assert.Equal(50, result.Telemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(150, result.Telemetry.TokenUsage.TotalTokenCount);
        Assert.Equal(20, result.Telemetry.TokenUsage.CachedInputTokenCount);
        Assert.Equal(10, result.Telemetry.TokenUsage.ReasoningTokenCount);
    }

    [Fact]
    public async Task ExecuteAsync_UsageWithZeroValues_Retained()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 0,
            OutputTokenCount = 0,
            TotalTokenCount = 0,
            CachedInputTokenCount = 0,
            ReasoningTokenCount = 0
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
            {
                Usage = usage
            }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry?.TokenUsage);
        Assert.Equal(0, result.Telemetry.TokenUsage.InputTokenCount);
        Assert.Equal(0, result.Telemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(0, result.Telemetry.TokenUsage.TotalTokenCount);
        Assert.Equal(0, result.Telemetry.TokenUsage.CachedInputTokenCount);
        Assert.Equal(0, result.Telemetry.TokenUsage.ReasoningTokenCount);
    }

    [Fact]
    public async Task ExecuteAsync_TotalDoesNotEqualInputPlusOutput_IsPreserved()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 10,
            OutputTokenCount = 20,
            TotalTokenCount = 999
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry?.TokenUsage);
        Assert.Equal(10, result.Telemetry.TokenUsage.InputTokenCount);
        Assert.Equal(20, result.Telemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(999, result.Telemetry.TokenUsage.TotalTokenCount);
    }

    [Fact]
    public async Task ExecuteAsync_NegativeInputTokenCount_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { InputTokenCount = -1 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_NegativeOutputTokenCount_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { OutputTokenCount = -1 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_NegativeTotalTokenCount_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { TotalTokenCount = -1 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_NegativeCachedInputTokenCount_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { CachedInputTokenCount = -1 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_NegativeReasoningTokenCount_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { ReasoningTokenCount = -1 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_CachedInputExceedsInput_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { InputTokenCount = 50, CachedInputTokenCount = 51 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_ReasoningExceedsOutput_ThrowsInvalidOperationException()
    {
        UsageDetails usage = new() { OutputTokenCount = 20, ReasoningTokenCount = 21 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidUsage_PreservesInnerValidationException()
    {
        UsageDetails usage = new() { InputTokenCount = -5 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteAsync(new ModelExecutionRequest("Test")));
        Assert.NotNull(ex.InnerException);
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }

    [Fact]
    public async Task ExecuteAsync_OneArgumentConstructor_DoesNotEstimateCost()
    {
        UsageDetails usage = new() { InputTokenCount = 100, OutputTokenCount = 50 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry?.TokenUsage);
        Assert.Null(result.Telemetry.EstimatedCost);
        Assert.Null(result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_EstimatesCostWithCompleteInputAndOutput()
    {
        UsageDetails usage = new() { InputTokenCount = 1_000_000, OutputTokenCount = 500_000 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry?.EstimatedCost);
        // (1M * 2.00 + 0.5M * 4.00) / 1M = (2 + 2) = 4.00
        Assert.Equal(4.00m, result.Telemetry.EstimatedCost);
        Assert.Equal("USD", result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_MissingInput_DoesNotEstimateCost()
    {
        UsageDetails usage = new() { InputTokenCount = null, OutputTokenCount = 500 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Null(result.Telemetry.EstimatedCost);
        Assert.Null(result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_MissingOutput_DoesNotEstimateCost()
    {
        UsageDetails usage = new() { InputTokenCount = 1000, OutputTokenCount = null };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Null(result.Telemetry.EstimatedCost);
        Assert.Null(result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_CachedPricingFormulaExact()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 1_000_000,
            CachedInputTokenCount = 400_000,
            OutputTokenCount = 200_000
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 3.00m, 5.00m, 1.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(3.20m, result.Telemetry.EstimatedCost);
        Assert.Equal("USD", result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_NullCachedPrice_FallsBackToInputPrice()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 1_000_000,
            CachedInputTokenCount = 400_000,
            OutputTokenCount = 500_000
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m, null);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(4.00m, result.Telemetry.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_ReasoningNotDoubleCharged()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 1_000_000,
            OutputTokenCount = 500_000,
            ReasoningTokenCount = 200_000
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(4.00m, result.Telemetry.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_TotalNotDoubleCharged()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 1_000_000,
            OutputTokenCount = 500_000,
            TotalTokenCount = 1_500_000
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(4.00m, result.Telemetry.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_DecimalEstimateNotAutomaticallyRounded()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 1,
            OutputTokenCount = 1
        };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("USD", 1.00m, 1.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(0.000002m, result.Telemetry.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_WithPricing_CostCurrencyCodeEqualsConfiguredCurrency()
    {
        UsageDetails usage = new() { InputTokenCount = 100, OutputTokenCount = 100 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage }
        };
        ModelTokenPricing pricing = new("BRL", 5.00m, 10.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal("BRL", result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_PricingIsNotDerivedFromResponseModelId()
    {
        UsageDetails usage = new() { InputTokenCount = 1_000_000, OutputTokenCount = 1_000_000 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
            {
                Usage = usage,
                ModelId = "gpt-4o"
            }
        };
        ModelTokenPricing pricing = new("USD", 1.00m, 1.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(2.00m, result.Telemetry.EstimatedCost);
    }

    [Fact]
    public async Task ExecuteAsync_PricingIsNotDerivedFromChatClientMetadata()
    {
        UsageDetails usage = new() { InputTokenCount = 1_000_000, OutputTokenCount = 1_000_000 };
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
            {
                Usage = usage
            }
        };
        ModelTokenPricing pricing = new("EUR", 2.00m, 3.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(5.00m, result.Telemetry.EstimatedCost);
        Assert.Equal("EUR", result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteAsync_DeterministicLatency_ZeroElapsed_ReturnsZeroLatency()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Equal(TimeSpan.Zero, result.Telemetry.Latency);
    }

    [Fact]
    public async Task ExecuteAsync_DeterministicLatency_AdvancedTime_ReturnsExactLatency()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        Task<ModelExecutionResult> task = runtime.ExecuteAsync(new ModelExecutionRequest("Test"));
        await callStarted.Task;

        TimeSpan advanceBy = TimeSpan.FromMilliseconds(250);
        timeProvider.Advance(advanceBy);
        callProceed.SetResult();

        ModelExecutionResult result = await task;

        Assert.NotNull(result.Telemetry);
        Assert.Equal(advanceBy, result.Telemetry.Latency);
    }

    [Fact]
    public async Task ExecuteAsync_TelemetryDoesNotIncludeWallClockTimestamp()
    {
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello"))
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        ModelExecutionResult result = await runtime.ExecuteAsync(new ModelExecutionRequest("Test"));

        Assert.NotNull(result.Telemetry);
        Assert.Null(typeof(ModelExecutionTelemetry).GetProperty("Timestamp"));
        Assert.Null(typeof(ModelExecutionTelemetry).GetProperty("CreatedAt"));
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ProviderCallCountRemainsOne_NonStreamingCallCountRemainsZero()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "chunk")]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
        }

        Assert.Equal(1, fakeClient.GetStreamingResponseCallCount);
        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ProviderDerivedItemsRetainOriginalTextAndHaveNullTelemetry()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "a"),
                new ChatResponseUpdate(ChatRole.Assistant, "b")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Equal(3, updates.Count);
        Assert.Equal("a", updates[0].ResponseTextDelta);
        Assert.Null(updates[0].Telemetry);
        Assert.Equal("b", updates[1].ResponseTextDelta);
        Assert.Null(updates[1].Telemetry);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_ExactlyOneFinalSyntheticTelemetryItemExistsAfterSuccess()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "hello")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Single(updates, u => u.Telemetry is not null);
        Assert.Same(updates.Last(), updates.Single(u => u.Telemetry is not null));
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FinalTelemetryItem_EmptyDeltaAndNonNullTelemetry()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "res")]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        ModelExecutionUpdate finalUpdate = updates.Last();
        Assert.Equal(string.Empty, finalUpdate.ResponseTextDelta);
        Assert.NotNull(finalUpdate.Telemetry);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FinalTelemetryWithNoUsage_HasNullTokenUsage()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "res")]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        ModelExecutionTelemetry finalTelemetry = updates.Last().Telemetry!;
        Assert.Null(finalTelemetry.TokenUsage);
        Assert.Null(finalTelemetry.EstimatedCost);
        Assert.Null(finalTelemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_EmptyProviderStream_YieldsExactlyOneFinalTelemetryItem()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = []
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Single(updates);
        Assert.Equal(string.Empty, updates[0].ResponseTextDelta);
        Assert.NotNull(updates[0].Telemetry);
        Assert.Null(updates[0].Telemetry!.TokenUsage);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_SingleUsageContent_MapsCorrectly()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 10,
            OutputTokenCount = 20,
            TotalTokenCount = 30
        };
        ChatResponseUpdate updateWithUsage = new(ChatRole.Assistant, "done")
        {
            Contents = [new UsageContent(usage)]
        };
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [updateWithUsage]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        ModelExecutionTelemetry finalTelemetry = updates.Last().Telemetry!;
        Assert.NotNull(finalTelemetry.TokenUsage);
        Assert.Equal(10, finalTelemetry.TokenUsage.InputTokenCount);
        Assert.Equal(20, finalTelemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(30, finalTelemetry.TokenUsage.TotalTokenCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_MultipleUsageContentValues_AreSummed()
    {
        ChatResponseUpdate update1 = new(ChatRole.Assistant, "part1")
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5, TotalTokenCount = 15 })]
        };
        ChatResponseUpdate update2 = new(ChatRole.Assistant, "part2")
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 5, OutputTokenCount = 15, TotalTokenCount = 20 })]
        };
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [update1, update2]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        ModelExecutionTelemetry finalTelemetry = updates.Last().Telemetry!;
        Assert.NotNull(finalTelemetry.TokenUsage);
        Assert.Equal(15, finalTelemetry.TokenUsage.InputTokenCount);
        Assert.Equal(20, finalTelemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(35, finalTelemetry.TokenUsage.TotalTokenCount);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_UsageOnlyProviderUpdate_YieldsEmptyTextUpdateAndNoIntermediateTelemetry()
    {
        ChatResponseUpdate usageOnly = new()
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 50, OutputTokenCount = 25 })]
        };
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [usageOnly]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        Assert.Equal(2, updates.Count);
        Assert.Equal(string.Empty, updates[0].ResponseTextDelta);
        Assert.Null(updates[0].Telemetry);
        Assert.Equal(string.Empty, updates[1].ResponseTextDelta);
        Assert.NotNull(updates[1].Telemetry);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FinalUsageNormalizedOnce_AndFinalCostUsesAggregateUsage()
    {
        ChatResponseUpdate chunk1 = new(ChatRole.Assistant, "a")
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 500_000, OutputTokenCount = 200_000 })]
        };
        ChatResponseUpdate chunk2 = new(ChatRole.Assistant, "b")
        {
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 500_000, OutputTokenCount = 300_000 })]
        };
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [chunk1, chunk2]
        };
        ModelTokenPricing pricing = new("USD", 1.00m, 2.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);

        List<ModelExecutionUpdate> updates = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")))
        {
            updates.Add(update);
        }

        ModelExecutionTelemetry finalTelemetry = updates.Last().Telemetry!;
        Assert.NotNull(finalTelemetry.TokenUsage);
        Assert.Equal(1_000_000, finalTelemetry.TokenUsage.InputTokenCount);
        Assert.Equal(500_000, finalTelemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(2.00m, finalTelemetry.EstimatedCost);
        Assert.Equal("USD", finalTelemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_FinalLatencyExactUnderManualTimeProvider()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new();
        TaskCompletionSource chunk1Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource chunk2Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.EnqueueStreamingSequence(() => CreateDelayedStream(chunk1Proceed, chunk2Proceed));

        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")).GetAsyncEnumerator();

        chunk1Proceed.SetResult();
        Assert.True(await enumerator.MoveNextAsync());

        TimeSpan advanceBy = TimeSpan.FromMilliseconds(150);
        timeProvider.Advance(advanceBy);
        chunk2Proceed.SetResult();

        Assert.True(await enumerator.MoveNextAsync()); // chunk2
        Assert.True(await enumerator.MoveNextAsync()); // final telemetry
        ModelExecutionUpdate finalUpdate = enumerator.Current;

        Assert.NotNull(finalUpdate.Telemetry);
        Assert.Equal(advanceBy, finalUpdate.Telemetry.Latency);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_InvalidAccumulatedUsage_ThrowsInvalidOperationException_AndYieldsNoFinalTelemetry()
    {
        ChatResponseUpdate chunk = new(ChatRole.Assistant, "done");
        chunk.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = -10 }));
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn = [chunk]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        IAsyncEnumerator<ModelExecutionUpdate> enumerator =
            runtime.ExecuteStreamingAsync(new ModelExecutionRequest("Test")).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync()); // provider chunk "done"
        Assert.Equal("done", enumerator.Current.ResponseTextDelta);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator.MoveNextAsync());
        Assert.NotNull(ex.InnerException);
        Assert.IsType<ArgumentOutOfRangeException>(ex.InnerException);
    }
}
