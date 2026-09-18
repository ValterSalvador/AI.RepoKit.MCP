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
}
