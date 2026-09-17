namespace AiRepoKit.Agents.Runtime.Tests;

using Microsoft.Extensions.AI;

internal sealed class FakeChatClient : IChatClient
{
    public bool Disposed
    {
        get;
        private set;
    }

    public int GetResponseCallCount
    {
        get;
        private set;
    }

    public IReadOnlyList<ChatMessage>? LastMessages
    {
        get;
        private set;
    }

    public ChatOptions? LastOptions
    {
        get;
        private set;
    }

    public CancellationToken LastCancellationToken
    {
        get;
        private set;
    }

    public ChatResponse ResponseToReturn
    {
        get;
        set;
    } = new(new ChatMessage(ChatRole.Assistant, "Default fake response"));

    public Exception? ExceptionToThrow
    {
        get;
        set;
    }

    public ChatClientMetadata Metadata => new("FakeChatClient");

    public void Dispose()
    {
        this.Disposed = true;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.GetResponseCallCount++;
        this.LastMessages = chatMessages.ToList().AsReadOnly();
        this.LastOptions = options;
        this.LastCancellationToken = cancellationToken;

        cancellationToken.ThrowIfCancellationRequested();

        if (this.ExceptionToThrow is not null)
        {
            throw this.ExceptionToThrow;
        }

        return Task.FromResult(this.ResponseToReturn);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }
}
