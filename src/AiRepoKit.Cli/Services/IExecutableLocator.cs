namespace AiRepoKit.Cli.Services;

public interface IExecutableLocator
{
    string? Find(string executableName);
}
