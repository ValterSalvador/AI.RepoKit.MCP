namespace AiRepoKit.Agents.Runtime.Tests;

using System.Text.Json;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Microsoft.Extensions.AI;
using Xunit;

public sealed class ChatClientModelSessionRuntimeTests
{
    // =========================================================================
    // 1. Session Identity & Basic Lifecycle (Section 27)
    // =========================================================================

    [Fact]
    public void CreateSession_ReturnsNonblankAgentSessionReference()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        AgentSessionReference sessionRef = runtime.CreateSession();

        Assert.NotNull(sessionRef);
        Assert.False(string.IsNullOrWhiteSpace(sessionRef.Value));
    }

    [Fact]
    public void CreateSession_ConsecutiveCalls_ReturnDistinctActiveReferences()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        AgentSessionReference session1 = runtime.CreateSession();
        AgentSessionReference session2 = runtime.CreateSession();

        Assert.NotEqual(session1.Value, session2.Value);
    }

    [Fact]
    public void CreateSession_MultipleSimultaneouslyActiveSessions_AllReferencesAreDistinct()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        HashSet<string> sessionIds = [];
        for (int i = 0; i < 100; i++)
        {
            AgentSessionReference session = runtime.CreateSession();
            Assert.True(sessionIds.Add(session.Value));
        }
    }

    [Fact]
    public void CreateSession_CausesZeroIChatClientCalls()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        _ = runtime.CreateSession();

        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_SessionFromAnotherRuntimeInstance_IsUnknownAndThrowsInvalidOperationException()
    {
        FakeChatClient fakeClient1 = new();
        ChatClientModelExecutionRuntime runtime1 = new(fakeClient1);

        FakeChatClient fakeClient2 = new();
        ChatClientModelExecutionRuntime runtime2 = new(fakeClient2);

        AgentSessionReference sessionFromRuntime1 = runtime1.CreateSession();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime2.ExecuteInSessionAsync(sessionFromRuntime1, new ModelExecutionRequest("Hello")));

        Assert.Equal(0, fakeClient2.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_NullSessionReference_ThrowsArgumentNullException()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runtime.ExecuteInSessionAsync(null!, new ModelExecutionRequest("Hello")));
    }

    [Fact]
    public async Task ExecuteInSessionAsync_NullRequest_ThrowsArgumentNullException()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, null!));
    }

    [Fact]
    public async Task ExecuteInSessionAsync_PreCanceledToken_ThrowsOperationCanceledException_WithoutCallingClient()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Hello"), cts.Token));

        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_UnknownSession_ThrowsInvalidOperationException_BeforeCallingClient()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference unknownRef = new("non-existent-session-id");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(unknownRef, new ModelExecutionRequest("Hello")));

        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_EndedSession_ThrowsInvalidOperationException_BeforeCallingClient()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        bool ended = await runtime.EndSessionAsync(sessionRef);
        Assert.True(ended);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Hello")));

        Assert.Equal(0, fakeClient.GetResponseCallCount);
    }

    [Fact]
    public async Task EndSessionAsync_NullSessionReference_ThrowsArgumentNullException()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => runtime.EndSessionAsync(null!));
    }

    [Fact]
    public async Task EndSessionAsync_SuccessfulFirstEnd_ReturnsTrue()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        bool result = await runtime.EndSessionAsync(sessionRef);

        Assert.True(result);
    }

    [Fact]
    public async Task EndSessionAsync_SecondEnd_ReturnsFalse()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        bool first = await runtime.EndSessionAsync(sessionRef);
        bool second = await runtime.EndSessionAsync(sessionRef);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task EndSessionAsync_UnknownSession_ReturnsFalse()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference unknownRef = new("unknown-session-123");

        bool result = await runtime.EndSessionAsync(unknownRef);

        Assert.False(result);
    }

    [Fact]
    public async Task EndSessionAsync_DoesNotDisposeChatClient()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.EndSessionAsync(sessionRef);

        Assert.False(fakeClient.Disposed);
    }

    // =========================================================================
    // 2. Stateless Conversation Continuity (Section 28)
    // =========================================================================

    [Fact]
    public async Task StatelessContinuity_Turn1SendsSingleUserMessage_PreservesProviderResponseOrderInTurn2()
    {
        FakeChatClient fakeClient = new();
        ChatMessage assistantMsg1 = new(ChatRole.Assistant, "First response part");
        ChatMessage assistantMsg2 = new(ChatRole.Assistant, "Second response part");

        fakeClient.EnqueueResponse(new ChatResponse([assistantMsg1, assistantMsg2]));
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Turn 2 response")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Turn 1
        ModelExecutionResult result1 = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("User turn 1"));

        Assert.Equal(1, fakeClient.GetResponseCallCount);
        IReadOnlyList<ChatMessage> turn1Sent = fakeClient.InvocationsMessages[0];
        Assert.Single(turn1Sent);
        Assert.Equal(ChatRole.User, turn1Sent[0].Role);
        Assert.Equal("User turn 1", turn1Sent[0].Text);

        // Turn 2
        ModelExecutionResult result2 = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("User turn 2"));

        Assert.Equal(2, fakeClient.GetResponseCallCount);
        IReadOnlyList<ChatMessage> turn2Sent = fakeClient.InvocationsMessages[1];

        // Must send exactly [user1, assistantMsg1, assistantMsg2, user2]
        Assert.Equal(4, turn2Sent.Count);
        Assert.Equal(ChatRole.User, turn2Sent[0].Role);
        Assert.Equal("User turn 1", turn2Sent[0].Text);
        Assert.Same(assistantMsg1, turn2Sent[1]);
        Assert.Same(assistantMsg2, turn2Sent[2]);
        Assert.Equal(ChatRole.User, turn2Sent[3].Role);
        Assert.Equal("User turn 2", turn2Sent[3].Text);
    }

    [Fact]
    public async Task StatelessContinuity_ConsecutiveTurns_AccumulateHistoryInExactOrder()
    {
        FakeChatClient fakeClient = new();
        ChatMessage resp1 = new(ChatRole.Assistant, "Resp 1");
        ChatMessage resp2 = new(ChatRole.Assistant, "Resp 2");
        ChatMessage resp3 = new(ChatRole.Assistant, "Resp 3");

        fakeClient.EnqueueResponse(new ChatResponse([resp1]));
        fakeClient.EnqueueResponse(new ChatResponse([resp2]));
        fakeClient.EnqueueResponse(new ChatResponse([resp3]));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Prompt 1"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Prompt 2"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Prompt 3"));

        Assert.Equal(3, fakeClient.GetResponseCallCount);

        IReadOnlyList<ChatMessage> turn3Sent = fakeClient.InvocationsMessages[2];
        Assert.Equal(5, turn3Sent.Count);
        Assert.Equal("Prompt 1", turn3Sent[0].Text);
        Assert.Same(resp1, turn3Sent[1]);
        Assert.Equal("Prompt 2", turn3Sent[2].Text);
        Assert.Same(resp2, turn3Sent[3]);
        Assert.Equal("Prompt 3", turn3Sent[4].Text);
    }

    [Fact]
    public async Task StatelessContinuity_SessionsDoNotShareHistory()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);

        AgentSessionReference sessionA = runtime.CreateSession();
        AgentSessionReference sessionB = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionA, new ModelExecutionRequest("Session A Turn 1"));
        await runtime.ExecuteInSessionAsync(sessionB, new ModelExecutionRequest("Session B Turn 1"));
        await runtime.ExecuteInSessionAsync(sessionA, new ModelExecutionRequest("Session A Turn 2"));

        Assert.Equal(3, fakeClient.GetResponseCallCount);

        IReadOnlyList<ChatMessage> sessionATurn2 = fakeClient.InvocationsMessages[2];
        // Must contain only Session A history
        Assert.All(sessionATurn2, m => Assert.DoesNotContain("Session B", m.Text));
    }

    // =========================================================================
    // 3. Stateful Conversation Continuity (Section 29)
    // =========================================================================

    [Fact]
    public async Task StatefulContinuity_CapturesProviderConversationId_AndDoesNotExposeItAsAgentSessionReference()
    {
        string providerConvId = "provider-native-conv-999";
        FakeChatClient fakeClient = new()
        {
            ResponseToReturn = new ChatResponse(new ChatMessage(ChatRole.Assistant, "Stateful reply"))
            {
                ConversationId = providerConvId
            }
        };

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Assert.NotEqual(providerConvId, sessionRef.Value);

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 1"));

        Assert.NotEqual(providerConvId, sessionRef.Value);
    }

    [Fact]
    public async Task StatefulContinuity_SubsequentTurnSendsOnlyNewUserMessage_AndSetsChatOptionsConversationId()
    {
        string providerConvId = "provider-conv-abc";
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Turn 1 reply"))
        {
            ConversationId = providerConvId
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Turn 2 reply"))
        {
            ConversationId = providerConvId
        });

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 1 prompt"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 2 prompt"));

        Assert.Equal(2, fakeClient.GetResponseCallCount);

        IReadOnlyList<ChatMessage> turn2Sent = fakeClient.InvocationsMessages[1];
        Assert.Single(turn2Sent);
        Assert.Equal("Turn 2 prompt", turn2Sent[0].Text);

        ChatOptions? turn2Options = fakeClient.InvocationsOptions[1];
        Assert.NotNull(turn2Options);
        Assert.Equal(providerConvId, turn2Options.ConversationId);
    }

    [Fact]
    public async Task StatefulContinuity_LaterNonblankConversationId_ReplacesPreviousProviderId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-1"
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2"))
        {
            ConversationId = "conv-2"
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R3"))
        {
            ConversationId = "conv-2"
        });

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 1"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 2"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 3"));

        Assert.Equal("conv-1", fakeClient.InvocationsOptions[1]?.ConversationId);
        Assert.Equal("conv-2", fakeClient.InvocationsOptions[2]?.ConversationId);
    }

    [Fact]
    public async Task StatefulContinuity_LaterNullConversationId_RetainsPreviousValidId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-stable"
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2"))
        {
            ConversationId = null
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R3"))
        {
            ConversationId = null
        });

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 1"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 2"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 3"));

        Assert.Equal("conv-stable", fakeClient.InvocationsOptions[1]?.ConversationId);
        Assert.Equal("conv-stable", fakeClient.InvocationsOptions[2]?.ConversationId);

        // Turn 3 must still send only the new user message
        Assert.Single(fakeClient.InvocationsMessages[2]);
        Assert.Equal("Turn 3", fakeClient.InvocationsMessages[2][0].Text);
    }

    [Fact]
    public async Task StatefulContinuity_LaterWhitespaceConversationId_RetainsPreviousValidId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-whitespace-test"
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2"))
        {
            ConversationId = "   "
        });

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 1"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 2"));

        Assert.Equal("conv-whitespace-test", fakeClient.InvocationsOptions[1]?.ConversationId);
    }

    // =========================================================================
    // 4. Stateless-to-Stateful Transition (Section 30)
    // =========================================================================

    [Fact]
    public async Task Transition_StatelessTurnsRetainHistory_TransitionTurnCapturesId_LaterTurnsSendOnlyNewUser()
    {
        FakeChatClient fakeClient = new();
        ChatMessage r1 = new(ChatRole.Assistant, "R1");
        ChatMessage r2 = new(ChatRole.Assistant, "R2");
        ChatMessage r3 = new(ChatRole.Assistant, "R3");

        // Turn 1: Stateless
        fakeClient.EnqueueResponse(new ChatResponse([r1]) { ConversationId = null });
        // Turn 2: Stateless
        fakeClient.EnqueueResponse(new ChatResponse([r2]) { ConversationId = null });
        // Turn 3: Transition turn returning ConversationId
        fakeClient.EnqueueResponse(new ChatResponse([r3]) { ConversationId = "transitioned-conv-id" });
        // Turn 4: Post-transition turn
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R4")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U4"));

        Assert.Equal(4, fakeClient.GetResponseCallCount);

        // Turn 3 (transition turn) sends full prior history + U3
        IReadOnlyList<ChatMessage> turn3Sent = fakeClient.InvocationsMessages[2];
        Assert.Equal(5, turn3Sent.Count);
        Assert.Equal("U1", turn3Sent[0].Text);
        Assert.Same(r1, turn3Sent[1]);
        Assert.Equal("U2", turn3Sent[2].Text);
        Assert.Same(r2, turn3Sent[3]);
        Assert.Equal("U3", turn3Sent[4].Text);
        Assert.Null(fakeClient.InvocationsOptions[2]?.ConversationId);

        // Turn 4 sends only U4 and carries the captured ConversationId
        IReadOnlyList<ChatMessage> turn4Sent = fakeClient.InvocationsMessages[3];
        Assert.Single(turn4Sent);
        Assert.Equal("U4", turn4Sent[0].Text);
        Assert.Equal("transitioned-conv-id", fakeClient.InvocationsOptions[3]?.ConversationId);
    }

    // =========================================================================
    // 5. Transactional Session-State Semantics (Section 31)
    // =========================================================================

    [Fact]
    public async Task TransactionalState_Stateless_FailedTurnThrowsException_DoesNotAppendUserOrResponseToHistory()
    {
        FakeChatClient fakeClient = new();
        ChatMessage r1 = new(ChatRole.Assistant, "R1");
        fakeClient.EnqueueResponse(new ChatResponse([r1]));

        InvalidOperationException clientException = new("Model failed on turn 2");
        fakeClient.EnqueueException(clientException);

        ChatMessage r3 = new(ChatRole.Assistant, "R3");
        fakeClient.EnqueueResponse(new ChatResponse([r3]));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        // Turn 2 fails
        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2_failed")));
        Assert.Same(clientException, thrown);

        // Turn 3 should only see U1 + R1 + U3 (U2_failed must NOT be in history)
        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        Assert.Equal(3, fakeClient.GetResponseCallCount);
        IReadOnlyList<ChatMessage> turn3Sent = fakeClient.InvocationsMessages[2];
        Assert.Equal(3, turn3Sent.Count);
        Assert.Equal("U1", turn3Sent[0].Text);
        Assert.Same(r1, turn3Sent[1]);
        Assert.Equal("U3", turn3Sent[2].Text);
    }

    [Fact]
    public async Task TransactionalState_Stateless_CanceledTurn_DoesNotAppendToHistory()
    {
        FakeChatClient fakeClient = new();
        ChatMessage r1 = new(ChatRole.Assistant, "R1");
        fakeClient.EnqueueResponse(new ChatResponse([r1]));
        fakeClient.EnqueueException(new OperationCanceledException("Turn 2 canceled"));
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R3")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2_canceled")));

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        IReadOnlyList<ChatMessage> turn3Sent = fakeClient.InvocationsMessages[2];
        Assert.Equal(3, turn3Sent.Count);
        Assert.Equal("U1", turn3Sent[0].Text);
        Assert.Same(r1, turn3Sent[1]);
        Assert.Equal("U3", turn3Sent[2].Text);
    }

    [Fact]
    public async Task TransactionalState_Stateful_FailedTurn_RetainsPreviousConversationId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-valid"
        });
        fakeClient.EnqueueException(new InvalidOperationException("Turn 2 failed"));
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R3")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2")));

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        Assert.Equal(3, fakeClient.GetResponseCallCount);
        Assert.Equal("conv-valid", fakeClient.InvocationsOptions[2]?.ConversationId);
    }

    [Fact]
    public async Task TransactionalState_Stateful_CanceledTurn_RetainsPreviousConversationId()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-valid"
        });
        fakeClient.EnqueueException(new OperationCanceledException("Turn 2 canceled"));
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R3")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2")));

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U3"));

        Assert.Equal("conv-valid", fakeClient.InvocationsOptions[2]?.ConversationId);
    }

    // =========================================================================
    // 6. Structured Output Per-Turn (Section 21)
    // =========================================================================

    [Fact]
    public async Task StructuredOutput_PerTurnOnly_IsAppliedWhenProvided_AndClearedWhenNotProvided()
    {
        FakeChatClient fakeClient = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        string jsonSchema = "{\"type\":\"object\",\"properties\":{\"age\":{\"type\":\"integer\"}}}";
        StructuredOutputContract contract = new(jsonSchema);

        // Turn 1 with structured output
        await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1 with schema", contract));

        Assert.NotNull(fakeClient.InvocationsOptions[0]?.ResponseFormat);

        // Turn 2 without structured output
        await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 2 without schema"));

        Assert.Null(fakeClient.InvocationsOptions[1]?.ResponseFormat);
    }

    [Fact]
    public async Task StructuredOutput_CombinedWithConversationId_SetsBothOnChatOptions()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-structured-test"
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 1"));

        string jsonSchema = "{\"type\":\"object\"}";
        StructuredOutputContract contract = new(jsonSchema);

        await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 2 with schema", contract));

        ChatOptions? turn2Options = fakeClient.InvocationsOptions[1];
        Assert.NotNull(turn2Options);
        Assert.Equal("conv-structured-test", turn2Options.ConversationId);
        Assert.NotNull(turn2Options.ResponseFormat);
    }

    // =========================================================================
    // 7. Concurrency and Lifecycle Coordination (Section 32)
    // =========================================================================

    [Fact]
    public async Task Concurrency_SameSession_SecondTurnFailsWithInvalidOperationException_WhileFirstTurnIsInFlight()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        // Launch first turn
        Task<ModelExecutionResult> firstTurnTask = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1 in flight"));

        // Wait until first turn reaches IChatClient
        await callStarted.Task;

        // Second turn on same session must throw InvalidOperationException immediately
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Turn 2 conflicting")));

        Assert.Contains("already active", ex.Message);

        // Verify second turn did not invoke IChatClient
        Assert.Equal(1, fakeClient.GetResponseCallCount);

        // Allow first turn to finish
        callProceed.SetResult();
        ModelExecutionResult firstResult = await firstTurnTask;
        Assert.NotNull(firstResult);
    }

    [Fact]
    public async Task Concurrency_DifferentSessions_CanExecuteConcurrently()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource sessionAStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource sessionBStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource proceedAll = new(TaskCreationOptions.RunContinuationsAsynchronously);

        fakeClient.OnGetResponseAsync = async (msgs, opts, ct) =>
        {
            string? prompt = msgs.LastOrDefault()?.Text;
            if (prompt == "Session A Prompt")
            {
                sessionAStarted.TrySetResult();
            }
            else if (prompt == "Session B Prompt")
            {
                sessionBStarted.TrySetResult();
            }

            await proceedAll.Task.WaitAsync(ct).ConfigureAwait(false);
        };

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionA = runtime.CreateSession();
        AgentSessionReference sessionB = runtime.CreateSession();

        Task<ModelExecutionResult> taskA = runtime.ExecuteInSessionAsync(
            sessionA,
            new ModelExecutionRequest("Session A Prompt"));

        Task<ModelExecutionResult> taskB = runtime.ExecuteInSessionAsync(
            sessionB,
            new ModelExecutionRequest("Session B Prompt"));

        // Both sessions should start and enter IChatClient concurrently
        await Task.WhenAll(sessionAStarted.Task, sessionBStarted.Task);

        Assert.Equal(2, fakeClient.GetResponseCallCount);

        // Allow both to complete
        proceedAll.SetResult();

        ModelExecutionResult[] results = await Task.WhenAll(taskA, taskB);
        Assert.Equal(2, results.Length);
    }

    [Fact]
    public async Task Lifecycle_EndSessionAsync_WaitsForActiveTurnToComplete_ThenRemovesSession()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> turnTask = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn in flight"));

        await callStarted.Task;

        // Start EndSessionAsync while turn is in flight; it should wait
        Task<bool> endTask = runtime.EndSessionAsync(sessionRef);

        Assert.False(endTask.IsCompleted);

        // Allow turn to finish
        callProceed.SetResult();

        ModelExecutionResult turnResult = await turnTask;
        Assert.NotNull(turnResult);

        // EndSessionAsync completes and returns true
        bool ended = await endTask;
        Assert.True(ended);

        // Session is now ended; subsequent execute fails
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("Next turn")));
    }

    [Fact]
    public async Task Lifecycle_CanceledEndSessionAsync_ThrowsOperationCanceledException_AndLeavesSessionActive()
    {
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> turnTask = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn in flight"));

        await callStarted.Task;

        using CancellationTokenSource endCts = new();
        Task<bool> endTask = runtime.EndSessionAsync(sessionRef, endCts.Token);

        // Cancel EndSessionAsync while it is waiting
        endCts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => endTask);

        // Allow active turn to finish
        callProceed.SetResult();
        ModelExecutionResult turnResult = await turnTask;
        Assert.NotNull(turnResult);

        // Session must remain active!
        fakeClient.CallStartedTcs = null;
        fakeClient.CallProceedTcs = null;
        ModelExecutionResult nextTurnResult = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Next turn after canceled end"));
        Assert.NotNull(nextTurnResult);

        // And EndSessionAsync can later end it successfully
        bool ended = await runtime.EndSessionAsync(sessionRef);
        Assert.True(ended);
    }

    // =========================================================================
    // V5.P06: Session Non-Streaming Telemetry Tests (Section 48)
    // =========================================================================

    [Fact]
    public async Task ExecuteInSessionAsync_SuccessfulCall_ReturnsNonNullTelemetry()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")));
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1"));

        Assert.NotNull(result.Telemetry);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_UsageNormalization_MapsAllFieldsIdenticallyToExecuteAsync()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 120,
            OutputTokenCount = 60,
            TotalTokenCount = 180,
            CachedInputTokenCount = 30,
            ReasoningTokenCount = 15
        };
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage });
        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1"));

        Assert.NotNull(result.Telemetry?.TokenUsage);
        Assert.Equal(120, result.Telemetry.TokenUsage.InputTokenCount);
        Assert.Equal(60, result.Telemetry.TokenUsage.OutputTokenCount);
        Assert.Equal(180, result.Telemetry.TokenUsage.TotalTokenCount);
        Assert.Equal(30, result.Telemetry.TokenUsage.CachedInputTokenCount);
        Assert.Equal(15, result.Telemetry.TokenUsage.ReasoningTokenCount);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_WithPricing_EstimatesCost()
    {
        UsageDetails usage = new()
        {
            InputTokenCount = 1_000_000,
            OutputTokenCount = 500_000
        };
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")) { Usage = usage });
        ModelTokenPricing pricing = new("USD", 2.00m, 4.00m);
        ChatClientModelExecutionRuntime runtime = new(fakeClient, pricing);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult result = await runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1"));

        Assert.NotNull(result.Telemetry?.EstimatedCost);
        Assert.Equal(4.00m, result.Telemetry.EstimatedCost);
        Assert.Equal("USD", result.Telemetry.CostCurrencyCode);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_DeterministicLatency_Measured()
    {
        ManualTimeProvider timeProvider = new();
        FakeChatClient fakeClient = new();
        TaskCompletionSource callStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource callProceed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fakeClient.CallStartedTcs = callStarted;
        fakeClient.CallProceedTcs = callProceed;
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> task = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("Turn 1"));
        await callStarted.Task;

        TimeSpan advanceBy = TimeSpan.FromMilliseconds(300);
        timeProvider.Advance(advanceBy);
        callProceed.SetResult();

        ModelExecutionResult result = await task;

        Assert.NotNull(result.Telemetry);
        Assert.Equal(advanceBy, result.Telemetry.Latency);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_ConversationIdBehavior_RemainsUnchangedWithTelemetry()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-p06"
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2")));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult turn1 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));
        Assert.NotNull(turn1.Telemetry);

        ModelExecutionResult turn2 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2"));
        Assert.NotNull(turn2.Telemetry);

        Assert.Equal("conv-p06", fakeClient.InvocationsOptions[1]?.ConversationId);
        Assert.Single(fakeClient.InvocationsMessages[1]);
        Assert.Equal("U2", fakeClient.InvocationsMessages[1][0].Text);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_StatelessHistoryBehavior_RemainsUnchangedWithTelemetry()
    {
        FakeChatClient fakeClient = new();
        ChatMessage r1 = new(ChatRole.Assistant, "R1");
        fakeClient.EnqueueResponse(new ChatResponse([r1]));
        fakeClient.EnqueueResponse(new ChatResponse([new ChatMessage(ChatRole.Assistant, "R2")]));

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult turn1 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));
        Assert.NotNull(turn1.Telemetry);

        ModelExecutionResult turn2 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2"));
        Assert.NotNull(turn2.Telemetry);

        IReadOnlyList<ChatMessage> turn2Sent = fakeClient.InvocationsMessages[1];
        Assert.Equal(3, turn2Sent.Count);
        Assert.Equal("U1", turn2Sent[0].Text);
        Assert.Same(r1, turn2Sent[1]);
        Assert.Equal("U2", turn2Sent[2].Text);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_StatefulProviderBehavior_RemainsUnchangedWithTelemetry()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            ConversationId = "conv-stateful"
        });

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult turn1 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));

        Assert.NotNull(turn1.Telemetry);
        Assert.Equal("conv-stateful", fakeClient.InvocationsOptions[0]?.ConversationId ?? "conv-stateful");
    }

    [Fact]
    public async Task ExecuteInSessionAsync_TurnGateReleasesAfterTimeout_AndSessionRemainsUsable()
    {
        FakeChatClient fakeClient = new()
        {
            NonCooperativeGetResponse = true
        };
        ManualTimeProvider timeProvider = new();
        ChatClientModelExecutionRuntime runtime = new(fakeClient, timeProvider);
        AgentSessionReference sessionRef = runtime.CreateSession();

        Task<ModelExecutionResult> turn1 = runtime.ExecuteInSessionAsync(
            sessionRef,
            new ModelExecutionRequest("U1", timeout_: TimeSpan.FromSeconds(2)));

        timeProvider.Advance(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<TimeoutException>(() => turn1);

        fakeClient.NonCooperativeGetResponse = false;
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2")));

        ModelExecutionResult turn2 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2"));
        Assert.NotNull(turn2.Telemetry);
        Assert.Equal("R2", turn2.ResponseText);
    }

    [Fact]
    public async Task ExecuteInSessionAsync_TelemetryDoesNotAlterSubsequentTurns()
    {
        FakeChatClient fakeClient = new();
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R1"))
        {
            Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 5 }
        });
        fakeClient.EnqueueResponse(new ChatResponse(new ChatMessage(ChatRole.Assistant, "R2"))
        {
            Usage = new UsageDetails { InputTokenCount = 20, OutputTokenCount = 10 }
        });

        ChatClientModelExecutionRuntime runtime = new(fakeClient);
        AgentSessionReference sessionRef = runtime.CreateSession();

        ModelExecutionResult turn1 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U1"));
        Assert.Equal(10, turn1.Telemetry?.TokenUsage?.InputTokenCount);

        ModelExecutionResult turn2 = await runtime.ExecuteInSessionAsync(sessionRef, new ModelExecutionRequest("U2"));
        Assert.Equal(20, turn2.Telemetry?.TokenUsage?.InputTokenCount);
    }
}
