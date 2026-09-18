namespace AiRepoKit.Agents.Runtime.Tests;

using System.Runtime.CompilerServices;
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

    public int GetStreamingResponseCallCount
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

    public IReadOnlyList<ChatMessage>? LastStreamingMessages
    {
        get;
        private set;
    }

    public ChatOptions? LastStreamingOptions
    {
        get;
        private set;
    }

    public CancellationToken LastStreamingCancellationToken
    {
        get;
        private set;
    }

    public List<IReadOnlyList<ChatMessage>> InvocationsStreamingMessages
    {
        get;
    } = [];

    public List<ChatOptions?> InvocationsStreamingOptions
    {
        get;
    } = [];

    public List<CancellationToken> InvocationsStreamingCancellationTokens
    {
        get;
    } = [];

    private readonly Queue<Func<Task<ChatResponse>>> _enqueuedResponses = new();
    private readonly Queue<Func<IAsyncEnumerable<ChatResponseUpdate>>> _enqueuedStreamingResponses = new();

    public void EnqueueResponse(ChatResponse response)
    {
        this._enqueuedResponses.Enqueue(() => Task.FromResult(response));
    }

    public void EnqueueException(Exception exception)
    {
        this._enqueuedResponses.Enqueue(() => Task.FromException<ChatResponse>(exception));
    }

    public void EnqueueStreamingUpdates(IEnumerable<ChatResponseUpdate> updates)
    {
        this._enqueuedStreamingResponses.Enqueue(() => ToAsyncEnumerable(updates));
    }

    public void EnqueueStreamingSequence(Func<IAsyncEnumerable<ChatResponseUpdate>> factory)
    {
        this._enqueuedStreamingResponses.Enqueue(factory);
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

    public TaskCompletionSource? StreamingCallStartedTcs
    {
        get;
        set;
    }

    public TaskCompletionSource? StreamingCallProceedTcs
    {
        get;
        set;
    }

    public bool NonCooperativeGetResponse
    {
        get;
        set;
    }

    public bool NonCooperativeMoveNext
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

    public List<ChatResponseUpdate>? StreamingUpdatesToReturn
    {
        get;
        set;
    }

    public Exception? ExceptionToThrow
    {
        get;
        set;
    }

    public Exception? StreamingExceptionToThrow
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

        if (this.NonCooperativeGetResponse)
        {
            TaskCompletionSource<ChatResponse> neverEnding = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return await neverEnding.Task.ConfigureAwait(false);
        }

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

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        this.GetStreamingResponseCallCount++;
        IReadOnlyList<ChatMessage> snapshot = chatMessages.ToList().AsReadOnly();
        this.LastStreamingMessages = snapshot;
        this.InvocationsStreamingMessages.Add(snapshot);
        this.LastStreamingOptions = options;
        this.InvocationsStreamingOptions.Add(options);
        this.LastStreamingCancellationToken = cancellationToken;
        this.InvocationsStreamingCancellationTokens.Add(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        this.StreamingCallStartedTcs?.TrySetResult();

        if (this.StreamingCallProceedTcs is not null)
        {
            await this.StreamingCallProceedTcs.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        if (this.StreamingExceptionToThrow is not null)
        {
            throw this.StreamingExceptionToThrow;
        }

        if (this._enqueuedStreamingResponses.Count > 0)
        {
            IAsyncEnumerable<ChatResponseUpdate> stream = this._enqueuedStreamingResponses.Dequeue()();
            await foreach (ChatResponseUpdate update in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        if (this.NonCooperativeMoveNext)
        {
            TaskCompletionSource neverEnding = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await neverEnding.Task.ConfigureAwait(false);
        }

        if (this.StreamingUpdatesToReturn is not null)
        {
            foreach (ChatResponseUpdate update in this.StreamingUpdatesToReturn)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return update;
            }
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                "Default fake streaming response");
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerable(
        IEnumerable<ChatResponseUpdate> updates)
    {
        foreach (ChatResponseUpdate update in updates)
        {
            yield return update;
        }

        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }
}

internal sealed class ThrowingDisposeAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private readonly IEnumerable<T> _items;
    private readonly Exception _disposalException;

    public ThrowingDisposeAsyncEnumerable(IEnumerable<T> items, Exception disposalException)
    {
        this._items = items;
        this._disposalException = disposalException;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        return new Enumerator(this._items.GetEnumerator(), this._disposalException);
    }

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private readonly IEnumerator<T> _inner;
        private readonly Exception _disposalException;

        public Enumerator(IEnumerator<T> inner, Exception disposalException)
        {
            this._inner = inner;
            this._disposalException = disposalException;
        }

        public T Current => this._inner.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            return ValueTask.FromResult(this._inner.MoveNext());
        }

        public ValueTask DisposeAsync()
        {
            this._inner.Dispose();
            throw this._disposalException;
        }
    }
}
