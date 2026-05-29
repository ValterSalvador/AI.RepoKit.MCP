namespace AiRepoKit.Cli.Models;

public sealed record RepoAnalysis
{
    public RepoAnalysis(
        RepoProfile profile_,
        IReadOnlyList<string> solutionFiles_,
        IReadOnlyList<string> projectFiles_,
        IReadOnlyList<string> detectedLanguages_,
        IReadOnlyList<string> repositoryTypes_,
        IReadOnlyList<string> repositorySignals_,
        bool hasGlobalJson_,
        bool hasAiDirectory_,
        bool hasToolsAiContext_,
        bool hasToolsAiContextMcp_,
        bool hasCodexConfig_,
        bool hasVsCodeMcpConfig_)
    {
        this.Profile = profile_;
        this.SolutionFiles = solutionFiles_;
        this.ProjectFiles = projectFiles_;
        this.DetectedLanguages = detectedLanguages_;
        this.RepositoryTypes = repositoryTypes_;
        this.RepositorySignals = repositorySignals_;
        this.HasGlobalJson = hasGlobalJson_;
        this.HasAiDirectory = hasAiDirectory_;
        this.HasToolsAiContext = hasToolsAiContext_;
        this.HasToolsAiContextMcp = hasToolsAiContextMcp_;
        this.HasCodexConfig = hasCodexConfig_;
        this.HasVsCodeMcpConfig = hasVsCodeMcpConfig_;
    }

    public RepoProfile Profile { get; }

    public IReadOnlyList<string> SolutionFiles { get; }

    public IReadOnlyList<string> ProjectFiles { get; }

    public IReadOnlyList<string> DetectedLanguages { get; }

    public IReadOnlyList<string> RepositoryTypes { get; }

    public IReadOnlyList<string> RepositorySignals { get; }

    public bool HasGlobalJson { get; }

    public bool HasAiDirectory { get; }

    public bool HasToolsAiContext { get; }

    public bool HasToolsAiContextMcp { get; }

    public bool HasCodexConfig { get; }

    public bool HasVsCodeMcpConfig { get; }
}
