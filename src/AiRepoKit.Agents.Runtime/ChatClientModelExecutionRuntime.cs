namespace AiRepoKit.Agents.Runtime;

using System.Collections.Concurrent;
using System.Text.Json;
using AiRepoKit.Agents;
using Microsoft.Extensions.AI;

public sealed class ChatClientModelExecutionRuntime : IModelSessionRuntime
{
    private readonly IChatClient _chatClient;
    private readonly ConcurrentDictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);

    public ChatClientModelExecutionRuntime(
        IChatClient chatClient_)
    {
        ArgumentNullException.ThrowIfNull(
            chatClient_,
            nameof(chatClient_));

        this._chatClient =
            chatClient_;
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

        ChatOptions? options = null;
        if (request_.StructuredOutput is not null)
        {
            using JsonDocument document =
                JsonDocument.Parse(request_.StructuredOutput.JsonSchema);

            JsonElement schemaElement =
                document.RootElement.Clone();

            options = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(schemaElement)
            };
        }

        ChatResponse response =
            await this._chatClient.GetResponseAsync(
                [userMessage],
                options,
                cancellationToken_).ConfigureAwait(false);

        return new ModelExecutionResult(
            response.Text ?? string.Empty);
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

            ChatResponse response =
                await this._chatClient.GetResponseAsync(
                    outgoingMessages,
                    options,
                    cancellationToken_).ConfigureAwait(false);

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
