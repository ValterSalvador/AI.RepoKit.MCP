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

    public List<IReadOnlyList<ChatMessage>> InvocationsMessages
    {
        get;
    } = [];

    public List<ChatOptions?> InvocationsOptions
    {
        get;
    } = [];

    public List<CancellationToken> InvocationsCancellationTokens
    {
        get;
    } = [];

    private readonly Queue<Func<Task<ChatResponse>>> _enqueuedResponses = new();

    public void EnqueueResponse(ChatResponse response)
    {
        this._enqueuedResponses.Enqueue(() => Task.FromResult(response));
    }

    public void EnqueueException(Exception exception)
    {
        this._enqueuedResponses.Enqueue(() => Task.FromException<ChatResponse>(exception));
    }

    public TaskCompletionSource? CallStartedTcs
    {
        get;
        set;
    }

    public TaskCompletionSource? CallProceedTcs
    {
        get;
        set;
    }

    public Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task>? OnGetResponseAsync
    {
        get;
        set;
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

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        this.GetResponseCallCount++;
        IReadOnlyList<ChatMessage> snapshot = chatMessages.ToList().AsReadOnly();
        this.LastMessages = snapshot;
        this.InvocationsMessages.Add(snapshot);
        this.LastOptions = options;
        this.InvocationsOptions.Add(options);
        this.LastCancellationToken = cancellationToken;
        this.InvocationsCancellationTokens.Add(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        this.CallStartedTcs?.TrySetResult();

        if (this.CallProceedTcs is not null)
        {
            await this.CallProceedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (this.OnGetResponseAsync is not null)
        {
            await this.OnGetResponseAsync(chatMessages, options, cancellationToken).ConfigureAwait(false);
        }

        if (this._enqueuedResponses.Count > 0)
        {
            return await this._enqueuedResponses.Dequeue()().ConfigureAwait(false);
        }

        if (this.ExceptionToThrow is not null)
        {
            throw this.ExceptionToThrow;
        }

        return this.ResponseToReturn;
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
