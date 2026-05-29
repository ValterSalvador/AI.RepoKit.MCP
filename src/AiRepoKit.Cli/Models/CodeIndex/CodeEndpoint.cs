namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeEndpoint(
    string Method,
    string Route,
    string File,
    int Line,
    string HandlerOrController,
    string SourceKind,
    string Preview);
