namespace AiRepoKit.Cli.Services;

public static class ScriptRuntimeFactory
{
    public static IScriptRunner CreateDefault()
    {
        var environmentAccessor = new EnvironmentAccessor();
        var locator = new PathExecutableLocator(environmentAccessor);
        var platformAccessor = new PlatformAccessor();
        var executableResolver = new ExecutableResolver(locator, platformAccessor);
        var processRunner = new ProcessRunner();
        return new ScriptRunner(executableResolver, processRunner);
    }
}
