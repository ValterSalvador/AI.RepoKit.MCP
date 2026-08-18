using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public interface IProcessRunner
{
    ProcessResult Run(string fileName, IEnumerable<string> arguments, string workingDirectory);
}
