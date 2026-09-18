namespace AiRepoKit.Agents.Runtime.Tests;

using System.Diagnostics;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Microsoft.Extensions.AI;
using Xunit;

public sealed class ChatClientModelStreamingSessionRuntimeTests
{
    // =========================================================================
    // Section 51: Session timeout tests
    // =========================================================================

    [Fact]
    public async Task ExecuteInSessionAsync_TimeoutExpires_ThrowsTimeoutException_AndCommitsNoHistory()
    {
        FakeChatClient fakeClient = new();
        ChatMessage r1 = new(ChatRole.Assistant, "R1");
        fakeClient.EnqueueResponse(new ChatResponse([r1]));

        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Turn 1 succeeds
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        // Turn 2 times out
        fakeClient.NonCooperativeGetResponse = true;
        Task<ModelExecutionResult> turn2Task = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("U2_timed_out", timeout_: TimeSpan.FromSeconds(5)));

        timeProvider.Advance(TimeSpan.FromSeconds(6));

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => turn2Task);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Turn 3 succeeds: history must contain only U1 + R1 + U3 (U2 not committed)
        fakeClient.NonCooperativeGetResponse = false;
        fakeClient.EnqueueResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "R3")]));

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        IReadOnlyList<ChatMessage> turn3Messages = fakeClient.InvocationsMessages[2];
        Assert.Equal(3, turn3Messages.Count);
        Assert.Equal("U1", turn3Messages[0].Text);
        Assert.Same(r1, turn3Messages[1]);
        Assert.Equal("U3", turn3Messages[2].Text);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_TimeoutExpires_ThrowsTimeoutException_AndRetainsPreviousConversationId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-prev"
        });

        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Turn 1 captures conversation ID
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        // Turn 2 times out
        fakeClient.NonCooperativeGetResponse = true;
        Task<ModelExecutionResult> turn2Task = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("U2_timed_out", timeout_: TimeSpan.FromSeconds(5)));

        timeProvider.Advance(TimeSpan.FromSeconds(6));

        await Assert.ThrowsAsync<TimeoutException>(() => turn2Task);

        // Turn 3 succeeds: options must still use "conv-prev"
        fakeClient.NonCooperativeGetResponse = false;
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R3")));

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        Assert.Equal("conv-prev", fakeClient.InvocationsOptions[2]?.ConversationId);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_TimeoutExpires_SessionRemainsActive_AndTurnGateReleased()
    {
        FakeChatClient fakeClient = new()
        {
            NonCooperativeGetResponse = true
        };

        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> turn1Task = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("U1", timeout_: TimeSpan.FromSeconds(3)));

        timeProvider.Advance(TimeSpan.FromSeconds(4));

        await Assert.ThrowsAsync<TimeoutException>(() => turn1Task);

        // Next turn immediately succeeds without being blocked by turn gate
        fakeClient.NonCooperativeGetResponse = false;
        fakeClient.ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Success after timeout"));

        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("U2"));

        Assert.Equal("Success after timeout", result.ResponseText);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_CallerCancellation_HasSameRollbackPropertiesAsTimeout()
    {
        FakeChatClient fakeClient = new();
        ChatMessage r1 = new(ChatRole.Assistant, "R1");
        fakeClient.EnqueueResponse(new ChatResponse([r1]));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Turn 1
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        // Turn 2 canceled
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        using CancellationTokenSource cts = new();
        Task<ModelExecutionResult> turn2Task = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("U2_canceled", timeout_: TimeSpan.FromMinutes(1)),
            cts.Token);

        await callStarted.Task;
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => turn2Task);

        // Turn 3 succeeds: history only has U1 + R1 + U3
        fakeClient.CallStartedTcs = null;
        fakeClient.CallProceedTcs = null;
        fakeClient.EnqueueResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "R3")]));

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        IReadOnlyList<ChatMessage> turn3Messages = fakeClient.InvocationsMessages[2];
        Assert.Equal(3, turn3Messages.Count);
        Assert.Equal("U1", turn3Messages[0].Text);
        Assert.Same(r1, turn3Messages[1]);
        Assert.Equal("U3", turn3Messages[2].Text);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_NonCooperativeProvider_RespectsCallerVisibleRuntimeTimeout_AndReleasesTurnGate()
    {
        FakeChatClient fakeClient = new()
        {
            NonCooperativeGetResponse = true
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> hangTask = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Hang prompt", timeout_: TimeSpan.FromSeconds(2)));

        timeProvider.Advance(TimeSpan.FromSeconds(3));

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => hangTask);
        Assert.Contains("timed out", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Turn gate is released, next cooperative call succeeds
        fakeClient.NonCooperativeGetResponse = false;
        fakeClient.ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Recovered"));

        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Next prompt"));

        Assert.Equal("Recovered", result.ResponseText);
    }

    // =========================================================================
    // Section 53: Session streaming continuity tests
    // =========================================================================

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Stateless_Turn1Input_IsUserOnly_AggregatesAndCommitsToHistory()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "Hello"),
                new ChatResponseUpdate(ChatRole.Assistant, " "),
                new ChatResponseUpdate(ChatRole.Assistant, "World")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        List<string> deltas = [];
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("User1 message")))
        {
            deltas.Add(update.ResponseTextDelta);
        }

        Assert.Equal(["Hello", " ", "World"], deltas);
        Assert.Equal(1, fakeClient.GetStreamingResponseCallCount);

        // Input sent to provider was user message only
        IReadOnlyList<ChatMessage> turn1Sent = fakeClient.InvocationsStreamingMessages[0];
        Assert.Single(turn1Sent);
        Assert.Equal("User1 message", turn1Sent[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Stateless_Turn2Input_ContainsCommittedHistoryPlusNewUser_InExactOrder()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Assistant 1")
        ]);
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Assistant 2")
        ]);

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Turn 1
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("User 1")))
        {
        }

        // Turn 2
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("User 2")))
        {
        }

        Assert.Equal(2, fakeClient.GetStreamingResponseCallCount);

        IReadOnlyList<ChatMessage> turn2Sent = fakeClient.InvocationsStreamingMessages[1];
        Assert.Equal(3, turn2Sent.Count);
        Assert.Equal(ChatRole.User, turn2Sent[0].Role);
        Assert.Equal("User 1", turn2Sent[0].Text);
        Assert.Equal(ChatRole.Assistant, turn2Sent[1].Role);
        Assert.Equal("Assistant 1", turn2Sent[1].Text);
        Assert.Equal(ChatRole.User, turn2Sent[2].Role);
        Assert.Equal("User 2", turn2Sent[2].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Stateful_CapturesConversationId_AndLaterStreamSendsOnlyNewUserWithConversationId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Response 1")
            {
                ConversationId = "stateful-conv-123"
            }
        ]);
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "Response 2")
        ]);

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Turn 1
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1 prompt")))
        {
        }

        // Turn 2
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 2 prompt")))
        {
        }

        Assert.Equal(2, fakeClient.GetStreamingResponseCallCount);

        // Turn 2 sent only Turn 2 user message with ConversationId
        IReadOnlyList<ChatMessage> turn2Messages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(turn2Messages);
        Assert.Equal("Turn 2 prompt", turn2Messages[0].Text);

        ChatOptions? turn2Options = fakeClient.InvocationsStreamingOptions[1];
        Assert.NotNull(turn2Options);
        Assert.Equal("stateful-conv-123", turn2Options.ConversationId);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Stateful_BlankStreamConversationId_RetainsPreviousConversationId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "R1")
            {
                ConversationId = "valid-conv-id"
            }
        ]);
        // Turn 2 returns whitespace conversation ID
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "R2")
            {
                ConversationId = "   "
            }
        ]);
        fakeClient.EnqueueStreamingUpdates(
        [
            new ChatResponseUpdate(ChatRole.Assistant, "R3")
        ]);

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, new ModelExecutionRequest("U1"))) { }
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, new ModelExecutionRequest("U2"))) { }
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, new ModelExecutionRequest("U3"))) { }

        // Turn 3 must still retain and send "valid-conv-id"
        Assert.Equal("valid-conv-id", fakeClient.InvocationsStreamingOptions[2]?.ConversationId);
    }

    // =========================================================================
    // Section 54: Stateless-to-stateful streaming transition test
    // =========================================================================

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_StatelessToStatefulTransition_ClearsLocalHistory_AndSubsequentTurnUsesProviderStateOnly()
    {
        FakeChatClient fakeClient = new();
        // Turn 1: stateless
        fakeClient.EnqueueStreamingUpdates([new ChatResponseUpdate(ChatRole.Assistant, "R1")]);
        // Turn 2: transition with ConversationId
        fakeClient.EnqueueStreamingUpdates([new ChatResponseUpdate(ChatRole.Assistant, "R2") { ConversationId = "transition-id" }]);
        // Turn 3: stateful
        fakeClient.EnqueueStreamingUpdates([new ChatResponseUpdate(ChatRole.Assistant, "R3")]);

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, new ModelExecutionRequest("U1"))) { }
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, new ModelExecutionRequest("U2"))) { }
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, new ModelExecutionRequest("U3"))) { }

        // Turn 2 sent U1 + R1 + U2
        IReadOnlyList<ChatMessage> turn2Messages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Equal(3, turn2Messages.Count);

        // Turn 3 sent ONLY U3 because local history was cleared after stateful transition
        IReadOnlyList<ChatMessage> turn3Messages = fakeClient.InvocationsStreamingMessages[2];
        Assert.Single(turn3Messages);
        Assert.Equal("U3", turn3Messages[0].Text);
        Assert.Equal("transition-id", fakeClient.InvocationsStreamingOptions[2]?.ConversationId);
    }

    // =========================================================================
    // Section 55: Streaming rollback tests
    // =========================================================================

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Rollback_ProviderExceptionAfterPartialUpdates_CommitsNoSessionState()
    {
        TaskCompletionSource errorTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateFailingStream(errorTcs));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        List<string> deltas = [];
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(
                sessionRef,
                new ModelExecutionRequest("Failing turn")))
            {
                deltas.Add(update.ResponseTextDelta);
                errorTcs.TrySetResult();
            }
        });

        Assert.Single(deltas);
        Assert.Equal("partial chunk", deltas[0]);

        // Next turn succeeds: sent messages must contain ONLY the new user message (no partial state)
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "clean turn")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Clean turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Clean turn", nextMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Rollback_CallerCancellationAfterPartialUpdates_CommitsNoSessionState()
    {
        TaskCompletionSource proceedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateCancellableStream(proceedTcs));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        using CancellationTokenSource cts = new();
        List<string> deltas = [];

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(
                sessionRef,
                new ModelExecutionRequest("Canceled turn", timeout_: TimeSpan.FromMinutes(1)),
                cts.Token))
            {
                deltas.Add(update.ResponseTextDelta);
                cts.Cancel();
                proceedTcs.TrySetResult();
            }
        });

        Assert.Single(deltas);

        // Next turn has empty history
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "clean")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Next turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Next turn", nextMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Rollback_RuntimeTimeoutAfterPartialUpdates_CommitsNoSessionState()
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
        AgentSessionReference sessionRef = runtime.CreateSession();

        IAsyncEnumerator<ModelExecutionUpdate> enumerator = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Timeout turn", timeout_: TimeSpan.FromSeconds(5))).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("chunk1", enumerator.Current.ResponseTextDelta);

        timeProvider.Advance(TimeSpan.FromSeconds(6));

        await Assert.ThrowsAsync<TimeoutException>(async () => await enumerator.MoveNextAsync());

        // Next turn succeeds and history contains NO partial state
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "recovery")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Recovery turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Recovery turn", nextMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_Rollback_EarlyConsumerBreakAfterPartialUpdates_CommitsNoSessionState_AndReleasesTurnLock()
    {
        FakeChatClient fakeClient = new()
        {
            StreamingUpdatesToReturn =
            [
                new ChatResponseUpdate(ChatRole.Assistant, "first update"),
                new ChatResponseUpdate(ChatRole.Assistant, "second update"),
                new ChatResponseUpdate(ChatRole.Assistant, "third update")
            ]
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Consumer breaks early after first update
        await foreach (ModelExecutionUpdate update in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Break early request")))
        {
            Assert.Equal("first update", update.ResponseTextDelta);
            break;
        }

        // Turn lock must be released, and next turn sees clean history (partial state discarded)
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "complete turn")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Subsequent turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Subsequent turn", nextMessages[0].Text);
    }

    // =========================================================================
    // Section 56: Streaming concurrency tests
    // =========================================================================

    [Fact]
    public async Task Concurrency_SameSession_StreamingVsStreaming_ThrowsInvalidOperationException_WithoutReachingClient()
    {
        TaskCompletionSource stream1Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateHoldingStream(stream1Proceed));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        IAsyncEnumerator<ModelExecutionUpdate> enumerator1 = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Stream 1")).GetAsyncEnumerator();

        Assert.True(await enumerator1.MoveNextAsync());

        // Stream 2 on same session must fail on MoveNextAsync
        IAsyncEnumerator<ModelExecutionUpdate> enumerator2 = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Stream 2")).GetAsyncEnumerator();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await enumerator2.MoveNextAsync());

        Assert.Contains("already active", ex.Message);
        Assert.Equal(1, fakeClient.GetStreamingResponseCallCount);

        // Allow stream 1 to finish
        stream1Proceed.SetResult();
        while (await enumerator1.MoveNextAsync()) { }
    }

    [Fact]
    public async Task Concurrency_SameSession_StreamingVsNonStreaming_ThrowsInvalidOperationException_WithoutReachingClient()
    {
        TaskCompletionSource stream1Proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateHoldingStream(stream1Proceed));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        IAsyncEnumerator<ModelExecutionUpdate> enumerator1 = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Stream 1")).GetAsyncEnumerator();

        Assert.True(await enumerator1.MoveNextAsync());

        // Non-streaming call on same session throws InvalidOperationException
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Non-stream turn")));

        Assert.Contains("already active", ex.Message);
        Assert.Equal(0, fakeClient.GetResponseCallCount);

        // Allow stream 1 to finish
        stream1Proceed.SetResult();
        while (await enumerator1.MoveNextAsync()) { }
    }

    [Fact]
    public async Task Concurrency_SameSession_NonStreamingVsStreaming_ThrowsInvalidOperationException_WithoutReachingClient()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> nonStreamTask = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Non-stream in flight"));

        await callStarted.Task;

        // Streaming call on same session throws InvalidOperationException
        IAsyncEnumerator<ModelExecutionUpdate> streamEnumerator = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Stream turn")).GetAsyncEnumerator();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await streamEnumerator.MoveNextAsync());

        Assert.Contains("already active", ex.Message);
        Assert.Equal(0, fakeClient.GetStreamingResponseCallCount);

        // Allow non-streaming call to finish
        callProceed.SetResult();
        await nonStreamTask;
    }

    [Fact]
    public async Task Concurrency_DifferentSessions_StreamingCanExecuteConcurrentlyWithoutGlobalLock()
    {
        TaskCompletionSource sessionAStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource sessionBStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource proceedAll = new(TaskCreationOptions.RunContinuationsAsynchronously);

        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateCoordinatedStream("Session A", sessionAStarted, proceedAll));
        fakeClient.EnqueueStreamingSequence(() => CreateCoordinatedStream("Session B", sessionBStarted, proceedAll));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionA = runtime.CreateSession();
        AgentSessionReference sessionB = runtime.CreateSession();

        IAsyncEnumerator<ModelExecutionUpdate> enumeratorA = runtime.ExecuteStreamingInSessionAsync(
            sessionA,
            new ModelExecutionRequest("Prompt A")).GetAsyncEnumerator();

        IAsyncEnumerator<ModelExecutionUpdate> enumeratorB = runtime.ExecuteStreamingInSessionAsync(
            sessionB,
            new ModelExecutionRequest("Prompt B")).GetAsyncEnumerator();

        Task<bool> moveNextATask = enumeratorA.MoveNextAsync().AsTask();
        Task<bool> moveNextBTask = enumeratorB.MoveNextAsync().AsTask();

        // Both sessions reach their streams concurrently
        await Task.WhenAll(sessionAStarted.Task, sessionBStarted.Task);

        proceedAll.SetResult();

        bool[] results = await Task.WhenAll(moveNextATask, moveNextBTask);
        Assert.All(results, Assert.True);
        Assert.Equal(2, fakeClient.GetStreamingResponseCallCount);
    }

    // =========================================================================
    // Section 57: EndSessionAsync with streaming
    // =========================================================================

    [Fact]
    public async Task EndSessionAsync_WhileStreamActive_WaitsForStreamToComplete_ThenEndsSession()
    {
        TaskCompletionSource streamProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateHoldingStream(streamProceed));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        IAsyncEnumerator<ModelExecutionUpdate> enumerator = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Active stream")).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());

        // EndSessionAsync starts while stream owns turn
        Task<bool> endTask = runtime.EndSessionAsync(sessionRef);
        Assert.False(endTask.IsCompleted);

        // Finish stream
        streamProceed.SetResult();
        while (await enumerator.MoveNextAsync()) { }

        // EndSession completes and returns true
        bool ended = await endTask;
        Assert.True(ended);

        // Subsequent call on session fails
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("After end")));
    }

    [Fact]
    public async Task EndSessionAsync_WhileStreamActive_CanceledEndSession_ThrowsOperationCanceledException_AndLeavesSessionActive()
    {
        TaskCompletionSource streamProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => CreateHoldingStream(streamProceed));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        IAsyncEnumerator<ModelExecutionUpdate> enumerator = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Active stream")).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());

        using CancellationTokenSource endCts = new();
        Task<bool> endTask = runtime.EndSessionAsync(sessionRef, endCts.Token);
        Assert.False(endTask.IsCompleted);

        // Cancel EndSessionAsync while waiting
        endCts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => endTask);

        // Finish stream
        streamProceed.SetResult();
        while (await enumerator.MoveNextAsync()) { }

        // Session must remain active
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "Active after cancel")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Next turn after cancel")))
        {
        }

        // And EndSessionAsync can later end it
        bool ended = await runtime.EndSessionAsync(sessionRef);
        Assert.True(ended);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateFailingStream(
        TaskCompletionSource errorTcs)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "partial chunk");
        await errorTcs.Task;
        throw new InvalidOperationException("Provider failed midway");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateCancellableStream(
        TaskCompletionSource proceedTcs)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "partial chunk");
        await proceedTcs.Task;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "second chunk");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateHoldingStream(
        TaskCompletionSource proceedTcs)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk1");
        await proceedTcs.Task;
        yield return new ChatResponseUpdate(ChatRole.Assistant, "chunk2");
    }

    [Fact]
    public async Task ExecuteInSessionAsync_ProviderThrowsTimeoutException_PropagatesUnchangedAndReleasesTurnGate()
    {
        TimeoutException providerTimeout = new("Provider session timeout");
        FakeChatClient fakeClient = new()
        {
            ExceptionToThrow = providerTimeout
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionRequest request = new("Prompt 1", timeout_: TimeSpan.FromSeconds(10));

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, request));

        Assert.Same(providerTimeout, thrown);

        // Verify turn gate was released and session state was NOT committed
        fakeClient.ExceptionToThrow = null;
        fakeClient.ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Success"));
        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Prompt 2"));

        Assert.Equal("Success", result.ResponseText);
        IReadOnlyList<ChatMessage> secondTurnMessages = fakeClient.InvocationsMessages[1];
        Assert.Single(secondTurnMessages);
        Assert.Equal("Prompt 2", secondTurnMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_ProviderThrowsTimeoutException_PropagatesUnchangedAndReleasesTurnGate()
    {
        TimeoutException providerTimeout = new("Provider streaming session timeout");
        FakeChatClient fakeClient = new()
        {
            StreamingExceptionToThrow = providerTimeout
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionRequest request = new("Stream prompt 1", timeout_: TimeSpan.FromSeconds(10));

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(sessionRef, request))
            {
            }
        });

        Assert.Same(providerTimeout, thrown);

        // Turn gate released and session remains usable
        fakeClient.StreamingExceptionToThrow = null;
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "Second turn success")];

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Stream prompt 2")))
        {
        }

        IReadOnlyList<ChatMessage> secondTurnMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(secondTurnMessages);
        Assert.Equal("Stream prompt 2", secondTurnMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_ProviderDisposeThrows_RollsBackSessionStateAndReleasesTurnGate()
    {
        InvalidOperationException disposalEx = new("Session stream disposal failure");
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => new ThrowingDisposeAsyncEnumerable<ChatResponseUpdate>(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "chunk1"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk2")
            ],
            disposalEx));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
                sessionRef,
                new ModelExecutionRequest("Turn with failing disposal")))
            {
            }
        });

        Assert.Same(disposalEx, thrown);

        // Verify that history was NOT committed, turn gate was released, and subsequent turn succeeds
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "Clean turn")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Subsequent clean turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Subsequent clean turn", nextMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_EarlyConsumerBreak_ProviderDisposeThrows_PropagatesAndRollsBack()
    {
        InvalidOperationException disposalEx = new("Session stream disposal failure on early break");
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueStreamingSequence(() => new ThrowingDisposeAsyncEnumerable<ChatResponseUpdate>(
            [
                new ChatResponseUpdate(ChatRole.Assistant, "chunk1"),
                new ChatResponseUpdate(ChatRole.Assistant, "chunk2")
            ],
            disposalEx));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
                sessionRef,
                new ModelExecutionRequest("Turn with early break and failing disposal")))
            {
                break;
            }
        });

        Assert.Same(disposalEx, thrown);

        // Verify that history was NOT committed, turn gate was released, and subsequent turn succeeds
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "Clean turn")];
        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Subsequent clean turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Subsequent clean turn", nextMessages[0].Text);
    }

    [Fact]
    public async Task ExecuteStreamingInSessionAsync_WithoutTimeout_NonCooperativeProvider_CallerCancellationRollsBackAndReleasesTurnGate()
    {
        FakeChatClient fakeClient = new()
        {
            NonCooperativeMoveNext = true
        };
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        using CancellationTokenSource callerCts = new();
        ModelExecutionRequest request = new("No-timeout non-cooperative session");

        IAsyncEnumerator<ModelExecutionUpdate> enumerator = runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            request,
            callerCts.Token).GetAsyncEnumerator();

        ValueTask<bool> moveNext = enumerator.MoveNextAsync();
        Assert.False(moveNext.IsCompleted);

        callerCts.Cancel();

        OperationCanceledException ex = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await moveNext);

        Assert.Equal(callerCts.Token, ex.CancellationToken);

        // Turn gate released and session remains usable
        fakeClient.NonCooperativeMoveNext = false;
        fakeClient.StreamingUpdatesToReturn = [new ChatResponseUpdate(ChatRole.Assistant, "Recovered turn")];

        await foreach (ModelExecutionUpdate _ in runtime.ExecuteStreamingInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Recovery turn")))
        {
        }

        IReadOnlyList<ChatMessage> nextMessages = fakeClient.InvocationsStreamingMessages[1];
        Assert.Single(nextMessages);
        Assert.Equal("Recovery turn", nextMessages[0].Text);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> CreateCoordinatedStream(
        string name,
        TaskCompletionSource startedTcs,
        TaskCompletionSource proceedTcs)
    {
        startedTcs.TrySetResult();
        await proceedTcs.Task;
        yield return new ChatResponseUpdate(ChatRole.Assistant, name);
    }
}
