namespace AiRepoKit.Agents.Runtime;

using System.Text.Json;
using Microsoft.Extensions.AI;

public sealed class ChatClientModelExecutionRuntime : IModelExecutionRuntime
{
    private readonly IChatClient _chatClient;

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
}
