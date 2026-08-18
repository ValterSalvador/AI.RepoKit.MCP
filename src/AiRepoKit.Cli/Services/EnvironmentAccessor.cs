namespace AiRepoKit.Cli.Services;

public sealed class EnvironmentAccessor : IEnvironmentAccessor
{
    public string? GetEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }
}
