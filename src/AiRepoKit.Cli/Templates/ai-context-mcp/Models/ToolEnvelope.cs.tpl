namespace {{McpNamespace}}.Models;

public sealed record ToolEnvelope<T>(
    T Data,
    int EstimatedSizeBytes,
    string TokenCostHint,
    bool SecretsExposed,
    bool SecretValuesReturned,
    bool RedactedOnly);
