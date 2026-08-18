namespace AiRepoKit.Cli.Models;

public sealed record ScriptDefinition(
    string Name,
    string? PowerShellRelativePath = null,
    string? BashRelativePath = null);
