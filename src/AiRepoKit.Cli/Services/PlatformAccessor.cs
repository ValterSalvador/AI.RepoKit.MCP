namespace AiRepoKit.Cli.Services;

public sealed class PlatformAccessor : IPlatformAccessor
{
    public bool IsWindows => OperatingSystem.IsWindows();
}
