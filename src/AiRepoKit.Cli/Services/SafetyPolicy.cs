namespace AiRepoKit.Cli.Services;

public sealed class SafetyPolicy
{
    public IReadOnlyList<string> GetExcludedPathSegments()
    {
        return
        [
            ".git",
            "appsettings",
            "bin",
            "docker-compose",
            "key",
            "migrations",
            "obj",
            "oracle-data",
            "sandboxes",
            "wwwroot"
        ];
    }
}
