namespace AiRepoKit.Cli.Services;

public sealed class PathExecutableLocator : IExecutableLocator
{
    private readonly IEnvironmentAccessor _environmentAccessor;

    public PathExecutableLocator(IEnvironmentAccessor environmentAccessor)
    {
        _environmentAccessor = environmentAccessor ?? throw new ArgumentNullException(nameof(environmentAccessor));
    }

    public string? Find(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        string? pathEnv = _environmentAccessor.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return null;
        }

        string[] paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string rawDir in paths)
        {
            string dir = rawDir.Trim('"');
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            try
            {
                string fullPath = Path.Combine(dir, executableName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore invalid PATH entries
            }
        }

        return null;
    }
}
