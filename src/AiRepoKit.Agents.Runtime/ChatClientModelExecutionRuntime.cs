namespace AiRepoKit.Agents.Runtime;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AiRepoKit.Agents;
using Microsoft.Extensions.AI;

public sealed class ChatClientModelExecutionRuntime : IModelStreamingExecutionRuntime
{
    private readonly IChatClient _chatClient;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    public ChatClientModelExecutionRuntime(
        IChatClient chatClient_)
        : this(chatClient_, TimeProvider.System)
    {
    }

    internal ChatClientModelExecutionRuntime(
        IChatClient chatClient_,
        TimeProvider timeProvider_)
    {
        ArgumentNullException.ThrowIfNull(
            chatClient_,
            nameof(chatClient_));

        ArgumentNullException.ThrowIfNull(
            timeProvider_,
            nameof(timeProvider_));

        this._chatClient =
            chatClient_;
        this._timeProvider =
            timeProvider_;
    }

    public async Task<ModelExecutionResult> ExecuteAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        cancellationToken_.ThrowIfCancellationRequested();

        ChatMessage userMessage =
            new(ChatRole.User, request_.Prompt);

        ChatOptions? options =
            CreateChatOptions(null, request_.StructuredOutput);

        if (request_.Timeout is null)
        {
            Task<ChatResponse> responseTask = this._chatClient.GetResponseAsync(
                [userMessage],
                options,
                cancellationToken_);

            ChatResponse response =
                await responseTask.WaitAsync(cancellationToken_).ConfigureAwait(false);

            return new ModelExecutionResult(
                response.Text ?? string.Empty);
        }

        TimeSpan timeout = request_.Timeout.Value;
        using CancellationTokenSource timeoutCts = new();
        using CancellationTokenSource effectiveCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken_, timeoutCts.Token);
        CancellationToken effectiveToken = effectiveCts.Token;

        using ITimer timer = this._timeProvider.CreateTimer(
            _ => timeoutCts.Cancel(),
            null,
            timeout,
            Timeout.InfiniteTimeSpan);

        Task<ChatResponse> responseTaskWithTimeout = this._chatClient.GetResponseAsync(
            [userMessage],
            options,
            effectiveToken);

        ChatResponse timedResponse;
        try
        {
            timedResponse = await responseTaskWithTimeout.WaitAsync(effectiveToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            if (cancellationToken_.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "The operation was canceled by the caller.",
                    ex,
                    cancellationToken_);
            }

            if (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"The operation timed out after {timeout.TotalMilliseconds} ms.",
                    ex);
            }

            throw;
        }

        return new ModelExecutionResult(
            timedResponse.Text ?? string.Empty);
    }

    public AgentSessionReference CreateSession()
    {
        while (true)
        {
            string candidateId = Guid.NewGuid().ToString("N");
            SessionState candidateSession = new(candidateId);
            if (this._sessions.TryAdd(candidateId, candidateSession))
            {
                return new AgentSessionReference(candidateId);
            }
        }
    }

    public async Task<ModelExecutionResult> ExecuteInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            sessionReference_,
            nameof(sessionReference_));

        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        cancellationToken_.ThrowIfCancellationRequested();

        if (!this._sessions.TryGetValue(sessionReference_.Value, out SessionState? session) || session.IsEnded)
        {
            throw new InvalidOperationException(
                $"Session '{sessionReference_.Value}' is unknown or has ended.");
        }

        if (!session.TryStartTurn())
        {
            if (session.IsEnded)
            {
                throw new InvalidOperationException(
                    $"Session '{sessionReference_.Value}' is unknown or has ended.");
            }

            throw new InvalidOperationException(
                $"A turn is already active for session '{sessionReference_.Value}'.");
        }

        try
        {
            cancellationToken_.ThrowIfCancellationRequested();

            ChatMessage userMessage =
                new(ChatRole.User, request_.Prompt);

            List<ChatMessage> outgoingMessages;
            if (session.ConversationId is not null)
            {
                outgoingMessages = [userMessage];
            }
            else
            {
                outgoingMessages = new List<ChatMessage>(session.History.Count + 1);
                outgoingMessages.AddRange(session.History);
                outgoingMessages.Add(userMessage);
            }

            ChatOptions? options =
                CreateChatOptions(session.ConversationId, request_.StructuredOutput);

            ChatResponse response;
            if (request_.Timeout is null)
            {
                Task<ChatResponse> responseTask = this._chatClient.GetResponseAsync(
                    outgoingMessages,
                    options,
                    cancellationToken_);

                response =
                    await responseTask.WaitAsync(cancellationToken_).ConfigureAwait(false);
            }
            else
            {
                TimeSpan timeout = request_.Timeout.Value;
                using CancellationTokenSource timeoutCts = new();
                using CancellationTokenSource effectiveCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken_, timeoutCts.Token);
                CancellationToken effectiveToken = effectiveCts.Token;

                using ITimer timer = this._timeProvider.CreateTimer(
                    _ => timeoutCts.Cancel(),
                    null,
                    timeout,
                    Timeout.InfiniteTimeSpan);

                Task<ChatResponse> responseTask = this._chatClient.GetResponseAsync(
                    outgoingMessages,
                    options,
                    effectiveToken);

                try
                {
                    response = await responseTask.WaitAsync(effectiveToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    if (cancellationToken_.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(
                            "The operation was canceled by the caller.",
                            ex,
                            cancellationToken_);
                    }

                    if (timeoutCts.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"The operation timed out after {timeout.TotalMilliseconds} ms.",
                            ex);
                    }

                    throw;
                }
            }

            if (!string.IsNullOrWhiteSpace(response.ConversationId))
            {
                session.ConversationId = response.ConversationId;
                session.History.Clear();
            }
            else if (session.ConversationId is null)
            {
                session.History.Add(userMessage);
                if (response.Messages is not null)
                {
                    foreach (ChatMessage message in response.Messages)
                    {
                        session.History.Add(message);
                    }
                }
            }

            return new ModelExecutionResult(
                response.Text ?? string.Empty);
        }
        finally
        {
            session.EndTurn();
        }
    }

    public IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        return ExecuteStreamingCoreAsync(request_, cancellationToken_);
    }

    private async IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingCoreAsync(
        ModelExecutionRequest request_,
        CancellationToken methodToken_,
        [EnumeratorCancellation] CancellationToken enumeratorToken_ = default)
    {
        using CancellationTokenSource combinedCallerCts =
            CancellationTokenSource.CreateLinkedTokenSource(methodToken_, enumeratorToken_);
        CancellationToken callerToken = combinedCallerCts.Token;

        callerToken.ThrowIfCancellationRequested();

        ChatMessage userMessage =
            new(ChatRole.User, request_.Prompt);

        ChatOptions? options =
            CreateChatOptions(null, request_.StructuredOutput);

        CancellationTokenSource? timeoutCts = null;
        CancellationTokenSource? effectiveCts = null;
        ITimer? timer = null;
        CancellationToken effectiveToken = callerToken;
        long startTimestamp = 0;

        if (request_.Timeout.HasValue)
        {
            TimeSpan timeout = request_.Timeout.Value;
            startTimestamp = this._timeProvider.GetTimestamp();
            timeoutCts = new CancellationTokenSource();
            effectiveCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutCts.Token);
            effectiveToken = effectiveCts.Token;
            timer = this._timeProvider.CreateTimer(
                _ => timeoutCts.Cancel(),
                null,
                timeout,
                Timeout.InfiniteTimeSpan);
        }

        try
        {
            IAsyncEnumerable<ChatResponseUpdate> streamingResponse =
                this._chatClient.GetStreamingResponseAsync(
                    [userMessage],
                    options,
                    effectiveToken);

            IAsyncEnumerator<ChatResponseUpdate> enumerator =
                streamingResponse.GetAsyncEnumerator(effectiveToken);

            Exception? primaryFailure = null;

            try
            {
                while (true)
                {
                    ChatResponseUpdate update;
                    try
                    {
                        if (callerToken.IsCancellationRequested)
                        {
                            CancellationToken reportedToken = methodToken_.IsCancellationRequested
                                ? methodToken_
                                : (enumeratorToken_.IsCancellationRequested ? enumeratorToken_ : callerToken);

                            throw new OperationCanceledException(
                                "The streaming execution was canceled by the caller.",
                                reportedToken);
                        }

                        if (request_.Timeout.HasValue)
                        {
                            TimeSpan elapsed = this._timeProvider.GetElapsedTime(startTimestamp);
                            bool isTimeout = timeoutCts!.IsCancellationRequested || elapsed >= request_.Timeout.Value;
                            if (isTimeout)
                            {
                                if (callerToken.IsCancellationRequested)
                                {
                                    CancellationToken reportedToken = methodToken_.IsCancellationRequested
                                        ? methodToken_
                                        : (enumeratorToken_.IsCancellationRequested ? enumeratorToken_ : callerToken);

                                    throw new OperationCanceledException(
                                        "The streaming execution was canceled by the caller.",
                                        reportedToken);
                                }

                                throw new TimeoutException(
                                    $"The streaming execution timed out after {request_.Timeout.Value.TotalMilliseconds} ms.");
                            }
                        }

                        ValueTask<bool> moveNextValueTask = enumerator.MoveNextAsync();
                        bool hasNext;
                        if (moveNextValueTask.IsCompletedSuccessfully)
                        {
                            hasNext = moveNextValueTask.Result;
                        }
                        else
                        {
                            try
                            {
                                hasNext = await moveNextValueTask.AsTask().WaitAsync(effectiveToken).ConfigureAwait(false);
                            }
                            catch (OperationCanceledException ex)
                            {
                                if (callerToken.IsCancellationRequested)
                                {
                                    CancellationToken reportedToken = methodToken_.IsCancellationRequested
                                        ? methodToken_
                                        : (enumeratorToken_.IsCancellationRequested ? enumeratorToken_ : callerToken);

                                    throw new OperationCanceledException(
                                        "The streaming execution was canceled by the caller.",
                                        ex,
                                        reportedToken);
                                }

                                bool isTimeout = timeoutCts is not null &&
                                    (timeoutCts.IsCancellationRequested ||
                                     (request_.Timeout.HasValue && this._timeProvider.GetElapsedTime(startTimestamp) >= request_.Timeout.Value));

                                if (isTimeout)
                                {
                                    throw new TimeoutException(
                                        $"The streaming execution timed out after {request_.Timeout!.Value.TotalMilliseconds} ms.",
                                        ex);
                                }

                                throw;
                            }
                        }

                        if (!hasNext)
                        {
                            break;
                        }

                        update = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        primaryFailure = ex;
                        throw;
                    }

                    yield return new ModelExecutionUpdate(update.Text ?? string.Empty);
                }
            }
            finally
            {
                if (primaryFailure is not null)
                {
                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // Primary failure is active; suppress secondary disposal failure.
                    }
                }
                else
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            timer?.Dispose();
            effectiveCts?.Dispose();
            timeoutCts?.Dispose();
        }
    }

    public IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            sessionReference_,
            nameof(sessionReference_));

        ArgumentNullException.ThrowIfNull(
            request_,
            nameof(request_));

        if (!this._sessions.TryGetValue(sessionReference_.Value, out SessionState? session) || session.IsEnded)
        {
            throw new InvalidOperationException(
                $"Session '{sessionReference_.Value}' is unknown or has ended.");
        }

        return ExecuteStreamingInSessionCoreAsync(session, sessionReference_, request_, cancellationToken_);
    }

    private async IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionCoreAsync(
        SessionState session_,
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken methodToken_,
        [EnumeratorCancellation] CancellationToken enumeratorToken_ = default)
    {
        using CancellationTokenSource combinedCallerCts =
            CancellationTokenSource.CreateLinkedTokenSource(methodToken_, enumeratorToken_);
        CancellationToken callerToken = combinedCallerCts.Token;

        callerToken.ThrowIfCancellationRequested();

        if (!session_.TryStartTurn())
        {
            if (session_.IsEnded)
            {
                throw new InvalidOperationException(
                    $"Session '{sessionReference_.Value}' is unknown or has ended.");
            }

            throw new InvalidOperationException(
                $"A turn is already active for session '{sessionReference_.Value}'.");
        }

        bool completedSuccessfully = false;
        List<ChatResponseUpdate> retainedUpdates = [];
        ChatMessage userMessage = new(ChatRole.User, request_.Prompt);

        try
        {
            callerToken.ThrowIfCancellationRequested();

            List<ChatMessage> outgoingMessages;
            if (session_.ConversationId is not null)
            {
                outgoingMessages = [userMessage];
            }
            else
            {
                outgoingMessages = new List<ChatMessage>(session_.History.Count + 1);
                outgoingMessages.AddRange(session_.History);
                outgoingMessages.Add(userMessage);
            }

            ChatOptions? options =
                CreateChatOptions(session_.ConversationId, request_.StructuredOutput);

            CancellationTokenSource? timeoutCts = null;
            CancellationTokenSource? effectiveCts = null;
            ITimer? timer = null;
            CancellationToken effectiveToken = callerToken;
            long startTimestamp = 0;

            if (request_.Timeout.HasValue)
            {
                TimeSpan timeout = request_.Timeout.Value;
                startTimestamp = this._timeProvider.GetTimestamp();
                timeoutCts = new CancellationTokenSource();
                effectiveCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutCts.Token);
                effectiveToken = effectiveCts.Token;
                timer = this._timeProvider.CreateTimer(
                    _ => timeoutCts.Cancel(),
                    null,
                    timeout,
                    Timeout.InfiniteTimeSpan);
            }

            try
            {
                IAsyncEnumerable<ChatResponseUpdate> streamingResponse =
                    this._chatClient.GetStreamingResponseAsync(
                        outgoingMessages,
                        options,
                        effectiveToken);

                IAsyncEnumerator<ChatResponseUpdate> enumerator =
                    streamingResponse.GetAsyncEnumerator(effectiveToken);

                Exception? primaryFailure = null;
                bool reachedEndOfStream = false;

                try
                {
                    while (true)
                    {
                        ChatResponseUpdate update;
                        try
                        {
                            if (callerToken.IsCancellationRequested)
                            {
                                CancellationToken reportedToken = methodToken_.IsCancellationRequested
                                    ? methodToken_
                                    : (enumeratorToken_.IsCancellationRequested ? enumeratorToken_ : callerToken);

                                throw new OperationCanceledException(
                                    "The streaming execution was canceled by the caller.",
                                    reportedToken);
                            }

                            if (request_.Timeout.HasValue)
                            {
                                TimeSpan elapsed = this._timeProvider.GetElapsedTime(startTimestamp);
                                bool isTimeout = timeoutCts!.IsCancellationRequested || elapsed >= request_.Timeout.Value;
                                if (isTimeout)
                                {
                                    if (callerToken.IsCancellationRequested)
                                    {
                                        CancellationToken reportedToken = methodToken_.IsCancellationRequested
                                            ? methodToken_
                                            : (enumeratorToken_.IsCancellationRequested ? enumeratorToken_ : callerToken);

                                        throw new OperationCanceledException(
                                            "The streaming execution was canceled by the caller.",
                                            reportedToken);
                                    }

                                    throw new TimeoutException(
                                        $"The streaming execution timed out after {request_.Timeout.Value.TotalMilliseconds} ms.");
                                }
                            }

                            ValueTask<bool> moveNextValueTask = enumerator.MoveNextAsync();
                            bool hasNext;
                            if (moveNextValueTask.IsCompletedSuccessfully)
                            {
                                hasNext = moveNextValueTask.Result;
                            }
                            else
                            {
                                try
                                {
                                    hasNext = await moveNextValueTask.AsTask().WaitAsync(effectiveToken).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException ex)
                                {
                                    if (callerToken.IsCancellationRequested)
                                    {
                                        CancellationToken reportedToken = methodToken_.IsCancellationRequested
                                            ? methodToken_
                                            : (enumeratorToken_.IsCancellationRequested ? enumeratorToken_ : callerToken);

                                        throw new OperationCanceledException(
                                            "The streaming execution was canceled by the caller.",
                                            ex,
                                            reportedToken);
                                    }

                                    bool isTimeout = timeoutCts is not null &&
                                        (timeoutCts.IsCancellationRequested ||
                                         (request_.Timeout.HasValue && this._timeProvider.GetElapsedTime(startTimestamp) >= request_.Timeout.Value));

                                    if (isTimeout)
                                    {
                                        throw new TimeoutException(
                                            $"The streaming execution timed out after {request_.Timeout!.Value.TotalMilliseconds} ms.",
                                            ex);
                                    }

                                    throw;
                                }
                            }

                            if (!hasNext)
                            {
                                reachedEndOfStream = true;
                                break;
                            }

                            update = enumerator.Current;
                            retainedUpdates.Add(update);
                        }
                        catch (Exception ex)
                        {
                            primaryFailure = ex;
                            throw;
                        }

                        yield return new ModelExecutionUpdate(update.Text ?? string.Empty);
                    }
                }
                finally
                {
                    if (primaryFailure is not null)
                    {
                        try
                        {
                            await enumerator.DisposeAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            // Primary failure is active; suppress secondary disposal failure.
                        }
                    }
                    else
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                        if (reachedEndOfStream)
                        {
                            completedSuccessfully = true;
                        }
                    }
                }
            }
            finally
            {
                timer?.Dispose();
                effectiveCts?.Dispose();
                timeoutCts?.Dispose();
            }
        }
        finally
        {
            try
            {
                if (completedSuccessfully)
                {
                    ChatResponse aggregatedResponse = retainedUpdates.ToChatResponse();

                    if (!string.IsNullOrWhiteSpace(aggregatedResponse.ConversationId))
                    {
                        session_.ConversationId = aggregatedResponse.ConversationId;
                        session_.History.Clear();
                    }
                    else if (session_.ConversationId is null)
                    {
                        session_.History.Add(userMessage);
                        if (aggregatedResponse.Messages is not null)
                        {
                            foreach (ChatMessage message in aggregatedResponse.Messages)
                            {
                                session_.History.Add(message);
                            }
                        }
                    }
                }
            }
            finally
            {
                session_.EndTurn();
            }
        }
    }

    public async Task<bool> EndSessionAsync(
        AgentSessionReference sessionReference_,
        CancellationToken cancellationToken_ = default)
    {
        ArgumentNullException.ThrowIfNull(
            sessionReference_,
            nameof(sessionReference_));

        if (!this._sessions.TryGetValue(sessionReference_.Value, out SessionState? session) || session.IsEnded)
        {
            return false;
        }

        bool ended =
            await session.WaitForTurnAndEndAsync(
                cancellationToken_).ConfigureAwait(false);

        if (ended)
        {
            this._sessions.TryRemove(sessionReference_.Value, out _);
        }

        return ended;
    }

    private static ChatOptions? CreateChatOptions(
        string? conversationId_,
        StructuredOutputContract? structuredOutput_)
    {
        if (conversationId_ is null && structuredOutput_ is null)
        {
            return null;
        }

        ChatOptions options = new();

        if (conversationId_ is not null)
        {
            options.ConversationId = conversationId_;
        }

        if (structuredOutput_ is not null)
        {
            using JsonDocument document =
                JsonDocument.Parse(structuredOutput_.JsonSchema);

            JsonElement schemaElement =
                document.RootElement.Clone();

            options.ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaElement);
        }

        return options;
    }

    private sealed class SessionState
    {
        private readonly SemaphoreSlim _turnLock = new(1, 1);
        private readonly object _stateLock = new();
        private volatile bool _isEnded;
        private readonly List<ChatMessage> _history = [];
        private string? _conversationId;

        public string Id
        {
            get;
        }

        public bool IsEnded => this._isEnded;

        public List<ChatMessage> History => this._history;

        public string? ConversationId
        {
            get => this._conversationId;
            set => this._conversationId = value;
        }

        public SessionState(string id_)
        {
            this.Id = id_;
        }

        public bool TryStartTurn()
        {
            lock (this._stateLock)
            {
                if (this._isEnded)
                {
                    return false;
                }

                return this._turnLock.Wait(0);
            }
        }

        public void EndTurn()
        {
            this._turnLock.Release();
        }

        public async Task<bool> WaitForTurnAndEndAsync(
            CancellationToken cancellationToken_)
        {
            await this._turnLock.WaitAsync(cancellationToken_).ConfigureAwait(false);
            try
            {
                lock (this._stateLock)
                {
                    if (this._isEnded)
                    {
                        return false;
                    }

                    this._isEnded = true;
                    return true;
                }
            }
            finally
            {
                this._turnLock.Release();
            }
        }
    }
}
