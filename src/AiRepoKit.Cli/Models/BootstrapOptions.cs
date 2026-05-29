namespace AiRepoKit.Cli.Models;

public sealed record BootstrapOptions
{
    public BootstrapOptions(
        string command_,
        string repoPath_,
        IReadOnlyList<ClientKind> clients_,
        bool includeMcp_,
        bool apply_,
        bool dryRun_,
        bool backup_,
        bool force_,
        bool forceManaged_,
        string profile_,
        string targetFramework_,
        string mcpServerName_,
        string toolCommandName_,
        string mcpProjectName_,
        string mcpNamespace_,
        string mcpAssemblyName_,
        string mcpProjectRelativePath_,
        bool skipBuildMcp_,
        bool skipAiContext_,
        bool skipCodeInventory_,
        bool skipSecurityScan_,
        bool skipBudget_,
        bool skipSmoke_,
        bool skipScripts_,
        int maxFiles_,
        int maxItems_,
        bool includePrivateMembers_,
        bool noCache_,
        bool rebuildCache_,
        string output_,
        string format_,
        bool verbose_,
        bool auditJson_,
        bool includeSource_,
        bool createAuditBaseline_,
        bool updateAuditBaseline_,
        bool showAuditBaseline_,
        bool failOnAccepted_,
        bool skipAudit_,
        bool includeAgents_,
        string task_,
        string target_,
        int limit_,
        bool requireContextPacks_,
        IReadOnlyList<string> unknownOptions_,
        bool noProgress_ = false,
        bool refresh_ = false,
        bool noRefresh_ = false,
        string sampleQuery_ = "")
    {
        this.Command = command_;
        this.RepoPath = repoPath_;
        this.Clients = clients_;
        this.IncludeMcp = includeMcp_;
        this.Apply = apply_;
        this.DryRun = dryRun_;
        this.Backup = backup_;
        this.Force = force_;
        this.ForceManaged = forceManaged_;
        this.Profile = profile_;
        this.TargetFramework = targetFramework_;
        this.McpServerName = mcpServerName_;
        this.ToolCommandName = toolCommandName_;
        this.McpProjectName = mcpProjectName_;
        this.McpNamespace = mcpNamespace_;
        this.McpAssemblyName = mcpAssemblyName_;
        this.McpProjectRelativePath = mcpProjectRelativePath_;
        this.SkipBuildMcp = skipBuildMcp_;
        this.SkipAiContext = skipAiContext_;
        this.SkipCodeInventory = skipCodeInventory_;
        this.SkipSecurityScan = skipSecurityScan_;
        this.SkipBudget = skipBudget_;
        this.SkipSmoke = skipSmoke_;
        this.SkipScripts = skipScripts_;
        this.MaxFiles = maxFiles_;
        this.MaxItems = maxItems_;
        this.IncludePrivateMembers = includePrivateMembers_;
        this.NoCache = noCache_;
        this.RebuildCache = rebuildCache_;
        this.Output = output_;
        this.Format = format_;
        this.Verbose = verbose_;
        this.AuditJson = auditJson_;
        this.IncludeSource = includeSource_;
        this.CreateAuditBaseline = createAuditBaseline_;
        this.UpdateAuditBaseline = updateAuditBaseline_;
        this.ShowAuditBaseline = showAuditBaseline_;
        this.FailOnAccepted = failOnAccepted_;
        this.SkipAudit = skipAudit_;
        this.IncludeAgents = includeAgents_;
        this.Task = task_;
        this.Target = target_;
        this.Limit = limit_;
        this.RequireContextPacks = requireContextPacks_;
        this.UnknownOptions = unknownOptions_;
        this.NoProgress = noProgress_;
        this.Refresh = refresh_;
        this.NoRefresh = noRefresh_;
        this.SampleQuery = sampleQuery_;
    }

    public string Command { get; }

    public string RepoPath { get; }

    public IReadOnlyList<ClientKind> Clients { get; }

    public bool IncludeMcp { get; }

    public bool Apply { get; }

    public bool DryRun { get; }

    public bool Backup { get; }

    public bool Force { get; }

    public bool ForceManaged { get; }

    public string Profile { get; }

    public string TargetFramework { get; }

    public string McpServerName { get; }

    public string ToolCommandName { get; }

    public string McpProjectName { get; }

    public string McpNamespace { get; }

    public string McpAssemblyName { get; }

    public string McpProjectRelativePath { get; }

    public bool SkipBuildMcp { get; }

    public bool SkipAiContext { get; }

    public bool SkipCodeInventory { get; }

    public bool SkipSecurityScan { get; }

    public bool SkipBudget { get; }

    public bool SkipSmoke { get; }

    public bool SkipScripts { get; }

    public int MaxFiles { get; }

    public int MaxItems { get; }

    public bool IncludePrivateMembers { get; }

    public bool NoCache { get; }

    public bool RebuildCache { get; }

    public string Output { get; }

    public string Format { get; }

    public bool Verbose { get; }

    public bool AuditJson { get; }

    public bool IncludeSource { get; }

    public bool CreateAuditBaseline { get; }

    public bool UpdateAuditBaseline { get; }

    public bool ShowAuditBaseline { get; }

    public bool FailOnAccepted { get; }

    public bool SkipAudit { get; }

    public bool IncludeAgents { get; }

    public string Task { get; }

    public string Target { get; }

    public int Limit { get; }

    public bool RequireContextPacks { get; }

    public IReadOnlyList<string> UnknownOptions { get; }

    public bool NoProgress { get; }

    public bool Refresh { get; }

    public bool NoRefresh { get; }

    public string SampleQuery { get; }
}
