namespace AiRepoKit.Cli.Services.SdkAlignment;

public sealed class SdkAlignmentReport
{
    public string ExpectedTargetFramework { get; init; } = string.Empty;

    public string DotNetSdkVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> DotNetSdks { get; init; } = [];

    public IReadOnlyList<SdkAlignmentProject> Projects { get; init; } = [];
}
