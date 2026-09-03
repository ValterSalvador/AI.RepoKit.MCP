using AiRepoKit.Cli.Models.CodeIndex;

namespace AiRepoKit.Cli.Services.CodeIndex;

public interface ICodeIndexFreshnessService
{
    CodeIndexFreshnessResult Check(string repoRoot_, int maxFiles_);
}
