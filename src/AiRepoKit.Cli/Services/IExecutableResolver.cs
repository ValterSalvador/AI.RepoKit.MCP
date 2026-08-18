using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public interface IExecutableResolver
{
    ResolvedScriptExecutable Resolve(ScriptShell shell);
}
