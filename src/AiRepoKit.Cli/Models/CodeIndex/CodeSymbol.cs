namespace AiRepoKit.Cli.Models.CodeIndex;

public sealed record CodeMember(
    string Name,
    string Kind,
    string ReturnType,
    string Visibility,
    int Line,
    int ParameterCount);

public sealed record CodeSymbol(
    string Name,
    string Kind,
    string Namespace,
    string File,
    int Line,
    string Visibility,
    string Parent,
    IReadOnlyList<string> BaseTypes,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<CodeMember> Methods,
    IReadOnlyList<CodeMember> Properties,
    string Classification,
    bool IsPartial,
    bool IsStatic,
    bool IsAbstract,
    bool IsSealed,
    int GenericArity);
