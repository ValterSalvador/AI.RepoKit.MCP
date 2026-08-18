namespace AiRepoKit.Cli.Models;

public sealed record ResolvedScriptExecutable(
    ScriptShell Shell,
    ScriptExecutableKind Kind,
    string FileName);
