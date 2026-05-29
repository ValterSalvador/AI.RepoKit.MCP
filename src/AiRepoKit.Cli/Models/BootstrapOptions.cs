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
        string sampleQuery_ = "",
        bool profileExplicit_ = false,
        IReadOnlyList<string>? forbiddenTerms_ = null,
        string sanitizeTerm_ = "",
        string sanitizeReplacement_ = "",
        bool strict_ = false,
        string defaultsSummary_ = "",
        int budget_ = 0,
        string kind_ = "",
        string since_ = "",
        bool changedFiles_ = false)
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
        this.ProfileExplicit = profileExplicit_;
        this.ForbiddenTerms = forbiddenTerms_ ?? [];
        this.SanitizeTerm = sanitizeTerm_;
        this.SanitizeReplacement = sanitizeReplacement_;
        this.Strict = strict_;
        this.DefaultsSummary = defaultsSummary_;
        this.Budget = budget_;
        this.Kind = kind_;
        this.Since = since_;
        this.ChangedFiles = changedFiles_;
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

    public bool ProfileExplicit { get; }

    public IReadOnlyList<string> ForbiddenTerms { get; }

    public string SanitizeTerm { get; }

    public string SanitizeReplacement { get; }

    public bool Strict { get; }

    public string DefaultsSummary { get; }

    public int Budget { get; }

    public string Kind { get; }

    public string Since { get; }

    public bool ChangedFiles { get; }

    public BootstrapOptions With(
        string? command_ = null,
        bool? includeMcp_ = null,
        bool? apply_ = null,
        bool? dryRun_ = null,
        bool? backup_ = null,
        bool? skipBuildMcp_ = null,
        bool? skipCodeInventory_ = null,
        bool? skipBudget_ = null,
        bool? skipAudit_ = null,
        bool? includeAgents_ = null,
        string? task_ = null,
        bool? requireContextPacks_ = null)
    {
        return new BootstrapOptions(
            command_ ?? this.Command,
            this.RepoPath,
            this.Clients,
            includeMcp_ ?? this.IncludeMcp,
            apply_ ?? this.Apply,
            dryRun_ ?? this.DryRun,
            backup_ ?? this.Backup,
            this.Force,
            this.ForceManaged,
            this.Profile,
            this.TargetFramework,
            this.McpServerName,
            this.ToolCommandName,
            this.McpProjectName,
            this.McpNamespace,
            this.McpAssemblyName,
            this.McpProjectRelativePath,
            skipBuildMcp_ ?? this.SkipBuildMcp,
            this.SkipAiContext,
            skipCodeInventory_ ?? this.SkipCodeInventory,
            this.SkipSecurityScan,
            skipBudget_ ?? this.SkipBudget,
            this.SkipSmoke,
            this.SkipScripts,
            this.MaxFiles,
            this.MaxItems,
            this.IncludePrivateMembers,
            this.NoCache,
            this.RebuildCache,
            this.Output,
            this.Format,
            this.Verbose,
            this.AuditJson,
            this.IncludeSource,
            this.CreateAuditBaseline,
            this.UpdateAuditBaseline,
            this.ShowAuditBaseline,
            this.FailOnAccepted,
            skipAudit_ ?? this.SkipAudit,
            includeAgents_ ?? this.IncludeAgents,
            task_ ?? this.Task,
            this.Target,
            this.Limit,
            requireContextPacks_ ?? this.RequireContextPacks,
            this.UnknownOptions,
            this.NoProgress,
            this.Refresh,
            this.NoRefresh,
            this.SampleQuery,
            this.ProfileExplicit,
            this.ForbiddenTerms,
            this.SanitizeTerm,
            this.SanitizeReplacement,
            this.Strict,
            this.DefaultsSummary,
            this.Budget,
            this.Kind,
            this.Since,
            this.ChangedFiles);
    }
}
