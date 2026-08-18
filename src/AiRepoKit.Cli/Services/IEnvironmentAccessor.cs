namespace AiRepoKit.Cli.Services;

public interface IEnvironmentAccessor
{
    string? GetEnvironmentVariable(string name);
}
