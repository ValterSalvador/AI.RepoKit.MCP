using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public interface IScriptRunner
{
    ProcessResult RunScript(
        ScriptDefinition definition,
        ScriptShell requestedShell,
        string repositoryRoot,
        IEnumerable<string>? scriptArguments = null,
        string? workingDirectory = null);
}
