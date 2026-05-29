using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ManagedFiles;
using AiRepoKit.Cli.Models.Profiles;
using AiRepoKit.Cli.Services.ManagedFiles;
using AiRepoKit.Cli.Services.Profiles;

namespace AiRepoKit.Cli.Services;

public sealed class ConfigGenerator
{
    private readonly FileSystemService _fileSystemService = new();
    private readonly TemplateService _templateService = new();
    private readonly ManagedFilesService _managedFilesService = new();
    private readonly GeneratedFileClassifier _generatedFileClassifier = new();
    private readonly ProfileService _profileService = new();
    private static readonly IReadOnlyList<TemplatePlanItem> AiTemplates =
    [
        new("ai/README.md.tpl", ".ai/README.md", ".ai", "Create AI context README."),
        new("ai/ai-operating-rules.md.tpl", ".ai/ai-operating-rules.md", ".ai", "Create AI operating rules."),
        new("ai/automation-risks.md.tpl", ".ai/automation-risks.md", ".ai", "Create automation risk notes."),
        new("ai/build-profile.md.tpl", ".ai/build-profile.md", ".ai", "Create build profile."),
        new("ai/codex-usage-guide.md.tpl", ".ai/codex-usage-guide.md", ".ai", "Create Codex usage guide."),
        new("ai/project-map.md.tpl", ".ai/project-map.md", ".ai", "Create project map."),
        new("ai/sdk-profile.md.tpl", ".ai/sdk-profile.md", ".ai", "Create SDK profile."),
        new("ai/test-profile.md.tpl", ".ai/test-profile.md", ".ai", "Create test profile."),
        new("ai/tool-roadmap.md.tpl", ".ai/tool-roadmap.md", ".ai", "Create tool roadmap."),
        new("ai/manifests/mcp-context-manifest.json.tpl", ".ai/manifests/mcp-context-manifest.json", ".ai", "Create MCP context manifest."),
        new("ai/manifests/mcp-context-manifest.md.tpl", ".ai/manifests/mcp-context-manifest.md", ".ai", "Create manifest documentation."),
        new("ai/context-budget.json.tpl", ".ai/context-budget.json", ".ai", "Create context budget."),
        new("ai/playbooks/change-ui.md.tpl", ".ai/playbooks/change-ui.md", ".ai", "Create UI change playbook."),
        new("ai/playbooks/diagnose-build.md.tpl", ".ai/playbooks/diagnose-build.md", ".ai", "Create build diagnosis playbook."),
        new("ai/playbooks/update-package.md.tpl", ".ai/playbooks/update-package.md", ".ai", "Create package update playbook."),
        new("ai/playbooks/inspect-security.md.tpl", ".ai/playbooks/inspect-security.md", ".ai", "Create security inspection playbook.")
    ];

    private static readonly IReadOnlyList<TemplatePlanItem> ToolsAiContextTemplates =
    [
        new("tools-ai-context/UpdateAiContext.ps1.tpl", "Tools/AiContext/UpdateAiContext.ps1", "tools-ai-context", "Create AI context update script."),
        new("tools-ai-context/CheckSdkAlignment.ps1.tpl", "Tools/AiContext/CheckSdkAlignment.ps1", "tools-ai-context", "Create SDK alignment check script."),
        new("tools-ai-context/UpdateCodeInventory.ps1.tpl", "Tools/AiContext/UpdateCodeInventory.ps1", "tools-ai-context", "Create code inventory script."),
        new("tools-ai-context/InvokeBuildDiagnostics.ps1.tpl", "Tools/AiContext/InvokeBuildDiagnostics.ps1", "tools-ai-context", "Create build diagnostics script."),
        new("tools-ai-context/CheckSecrets.ps1.tpl", "Tools/AiContext/CheckSecrets.ps1", "tools-ai-context", "Create secrets check script."),
        new("tools-ai-context/MeasureMcpResponseBudget.ps1.tpl", "Tools/AiContext/MeasureMcpResponseBudget.ps1", "tools-ai-context", "Create MCP response budget script.")
    ];

    private static readonly IReadOnlyList<TemplatePlanItem> AiContextMcpTemplates =
    [
        new("ai-context-mcp/{{McpProjectName}}.csproj.tpl", "Tools/AiContextMcp/{{McpProjectName}}.csproj", "ai-context-mcp", "Create MCP project file."),
        new("ai-context-mcp/Program.cs.tpl", "Tools/AiContextMcp/Program.cs", "ai-context-mcp", "Create MCP server entry point."),
        new("ai-context-mcp/README.md.tpl", "Tools/AiContextMcp/README.md", "ai-context-mcp", "Create MCP README."),
        new("ai-context-mcp/MCP_USAGE.md.tpl", "Tools/AiContextMcp/MCP_USAGE.md", "ai-context-mcp", "Create MCP usage guide."),
        new("ai-context-mcp/Models/ContextDetail.cs.tpl", "Tools/AiContextMcp/Models/ContextDetail.cs", "ai-context-mcp", "Create context detail model."),
        new("ai-context-mcp/Models/ContextManifest.cs.tpl", "Tools/AiContextMcp/Models/ContextManifest.cs", "ai-context-mcp", "Create manifest model."),
        new("ai-context-mcp/Models/ToolEnvelope.cs.tpl", "Tools/AiContextMcp/Models/ToolEnvelope.cs", "ai-context-mcp", "Create tool envelope model."),
        new("ai-context-mcp/Services/ContextBudget.cs.tpl", "Tools/AiContextMcp/Services/ContextBudget.cs", "ai-context-mcp", "Create context budget service."),
        new("ai-context-mcp/Services/ContextRepository.cs.tpl", "Tools/AiContextMcp/Services/ContextRepository.cs", "ai-context-mcp", "Create context repository service."),
        new("ai-context-mcp/Services/SecretRedactor.cs.tpl", "Tools/AiContextMcp/Services/SecretRedactor.cs", "ai-context-mcp", "Create secret redactor service."),
        new("ai-context-mcp/Tools/RepositoryContextTools.cs.tpl", "Tools/AiContextMcp/Tools/RepositoryContextTools.cs", "ai-context-mcp", "Create MCP tools.")
    ];

    private static readonly IReadOnlyList<ProfileTemplatePlanItem> AgentTemplates =
    [
        new("ask", "github/agents/ask.agent.md.tpl", ".github/agents/ask.agent.md", "agents", "Create Ask agent."),
        new("plan", "github/agents/plan.agent.md.tpl", ".github/agents/plan.agent.md", "agents", "Create Plan agent."),
        new("implementer", "github/agents/implementer.agent.md.tpl", ".github/agents/implementer.agent.md", "agents", "Create Implementer agent."),
        new("reviewer", "github/agents/reviewer.agent.md.tpl", ".github/agents/reviewer.agent.md", "agents", "Create Reviewer agent."),
        new("test-fixer", "github/agents/test-fixer.agent.md.tpl", ".github/agents/test-fixer.agent.md", "agents", "Create Test Fixer agent."),
        new("security-reviewer", "github/agents/security-reviewer.agent.md.tpl", ".github/agents/security-reviewer.agent.md", "agents", "Create Security Reviewer agent."),
        new("migration-architect", "github/agents/migration-architect.agent.md.tpl", ".github/agents/migration-architect.agent.md", "agents", "Create Migration Architect agent."),
        new("datalayer-specialist", "github/agents/datalayer-specialist.agent.md.tpl", ".github/agents/datalayer-specialist.agent.md", "agents", "Create Datalayer Specialist agent."),
        new("api-reviewer", "github/agents/api-reviewer.agent.md.tpl", ".github/agents/api-reviewer.agent.md", "agents", "Create API Reviewer agent."),
        new("winforms-specialist", "github/agents/winforms-specialist.agent.md.tpl", ".github/agents/winforms-specialist.agent.md", "agents", "Create WinForms Specialist agent."),
        new("source-generator-specialist", "github/agents/source-generator-specialist.agent.md.tpl", ".github/agents/source-generator-specialist.agent.md", "agents", "Create Source Generator Specialist agent.")
    ];

    private static readonly IReadOnlyList<ProfileTemplatePlanItem> InstructionTemplates =
    [
        new("csharp", "github/instructions/csharp.instructions.md.tpl", ".github/instructions/csharp.instructions.md", "agents", "Create C# instructions."),
        new("tests", "github/instructions/tests.instructions.md.tpl", ".github/instructions/tests.instructions.md", "agents", "Create test instructions."),
        new("safe-change", "github/instructions/safe-change.instructions.md.tpl", ".github/instructions/safe-change.instructions.md", "agents", "Create safe change instructions."),
        new("aspnet-core", "github/instructions/aspnet-core.instructions.md.tpl", ".github/instructions/aspnet-core.instructions.md", "agents", "Create ASP.NET Core instructions."),
        new("legacy-dotnet", "github/instructions/legacy-dotnet.instructions.md.tpl", ".github/instructions/legacy-dotnet.instructions.md", "agents", "Create legacy .NET instructions."),
        new("oracle-datalayer", "github/instructions/oracle-datalayer.instructions.md.tpl", ".github/instructions/oracle-datalayer.instructions.md", "agents", "Create Oracle datalayer instructions."),
        new("winforms", "github/instructions/winforms.instructions.md.tpl", ".github/instructions/winforms.instructions.md", "agents", "Create WinForms instructions."),
        new("source-generator", "github/instructions/source-generator.instructions.md.tpl", ".github/instructions/source-generator.instructions.md", "agents", "Create source generator instructions."),
        new("demo", "github/instructions/demo.instructions.md.tpl", ".github/instructions/demo.instructions.md", "agents", "Create Demo instructions.")
    ];

    private static readonly IReadOnlyList<ProfileTemplatePlanItem> PromptTemplates =
    [
        new("review-risk", "github/prompts/review-risk.prompt.md.tpl", ".github/prompts/review-risk.prompt.md", "agents", "Create review risk prompt."),
        new("fix-bug", "github/prompts/fix-bug.prompt.md.tpl", ".github/prompts/fix-bug.prompt.md", "agents", "Create bug fix prompt."),
        new("generate-tests", "github/prompts/generate-tests.prompt.md.tpl", ".github/prompts/generate-tests.prompt.md", "agents", "Create test generation prompt."),
        new("migration-plan", "github/prompts/migration-plan.prompt.md.tpl", ".github/prompts/migration-plan.prompt.md", "agents", "Create migration plan prompt."),
        new("review-datasource-flow", "github/prompts/review-datasource-flow.prompt.md.tpl", ".github/prompts/review-datasource-flow.prompt.md", "agents", "Create datasource flow review prompt."),
        new("analyze-source-generator", "github/prompts/analyze-source-generator.prompt.md.tpl", ".github/prompts/analyze-source-generator.prompt.md", "agents", "Create source generator analysis prompt."),
        new("review-api-change", "github/prompts/review-api-change.prompt.md.tpl", ".github/prompts/review-api-change.prompt.md", "agents", "Create API change review prompt.")
    ];

    private static readonly IReadOnlyList<ProfileTemplatePlanItem> AlwaysAgentTemplates =
    [
        new("copilot-instructions", "github/copilot-instructions.md.tpl", ".github/copilot-instructions.md", "agents", "Create Copilot instructions."),
        new("agents-guide", "root/AGENTS.md.tpl", "AGENTS.md", "agents", "Create agent guide.")
    ];

    public IReadOnlyList<PlannedChange> PlanBaseChanges(RepoAnalysis analysis_, BootstrapOptions options_)
    {
        List<PlannedChange> changes = [];

        if (!analysis_.Profile.Exists)
        {
            changes.Add(this.CreateWarning(analysis_.Profile.RootPath, "Target repository path does not exist."));
            return changes;
        }

        this.AddPresencePlan(changes, analysis_, options_, ".ai", "AI context root", false);
        this.AddPresencePlan(changes, analysis_, options_, ".ai/manifests/mcp-context-manifest.json", "MCP context manifest", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContext/UpdateAiContext.ps1", "AI context updater", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContext/CheckSdkAlignment.ps1", "SDK alignment checker", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContext/UpdateCodeInventory.ps1", "code inventory updater", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContext/InvokeBuildDiagnostics.ps1", "build diagnostics helper", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContext/CheckSecrets.ps1", "secrets checker", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContext/MeasureMcpResponseBudget.ps1", "MCP response budget checker", true);
        this.AddPresencePlan(changes, analysis_, options_, "Tools/AiContextMcp", "MCP project root", false);
        this.AddPresencePlan(changes, analysis_, options_, ".codex/config.toml", "Codex configuration", true);
        this.AddPresencePlan(changes, analysis_, options_, ".vscode/mcp.json", "VS Code MCP configuration", true);

        if (analysis_.SolutionFiles.Count == 0)
        {
            changes.Add(this.CreateWarning(".", "No solution files found at repository root."));
        }
        else
        {
            changes.Add(this.CreateSkip(".", $"{analysis_.SolutionFiles.Count} solution file(s) detected.", "Repository discovery only."));
        }

        if (analysis_.ProjectFiles.Count == 0)
        {
            changes.Add(this.CreateWarning(".", "No C# project files found."));
        }
        else
        {
            changes.Add(this.CreateSkip(".", $"{analysis_.ProjectFiles.Count} C# project file(s) detected.", "Repository discovery only."));
        }

        return changes;
    }

    public IReadOnlyList<PlannedChange> PlanInitChanges(RepoAnalysis analysis_, BootstrapOptions options_)
    {
        List<PlannedChange> changes = [];

        if (!analysis_.Profile.Exists)
        {
            changes.Add(this.CreateWarning(analysis_.Profile.RootPath, "Target repository path does not exist."));
            return changes;
        }

        ManagedFilesManifest manifest = this._managedFilesService.Load(analysis_.Profile.RootPath);
        string templateVersion = this._managedFilesService.GetToolVersion();

        changes.Add(this.CreateDirectoryChange(analysis_, options_, ".ai", "Create AI context root."));
        changes.AddRange(AiTemplates.Select(item_ => this.CreateManagedFileChange(
            analysis_,
            options_,
            manifest,
            item_.TemplatePath,
            templateVersion,
            RenderPath(item_.DestinationPath, options_),
            item_.Description)));
        changes.Add(this.CreateGitIgnoreChange(analysis_, options_));

        if (options_.IncludeMcp)
        {
            changes.Add(this.CreateDirectoryChange(analysis_, options_, "Tools/AiContext", "Create AI context tools root."));
            changes.Add(this.CreateDirectoryChange(analysis_, options_, "Tools/AiContextMcp", "Create MCP project root."));
            changes.AddRange(ToolsAiContextTemplates.Select(item_ => this.CreateManagedFileChange(
                analysis_,
                options_,
                manifest,
                item_.TemplatePath,
                templateVersion,
                RenderPath(item_.DestinationPath, options_),
                item_.Description)));
            changes.AddRange(AiContextMcpTemplates.Select(item_ => this.CreateManagedFileChange(
                analysis_,
                options_,
                manifest,
                item_.TemplatePath,
                templateVersion,
                RenderPath(item_.DestinationPath, options_),
                item_.Description)));
        }

        if (options_.IncludeAgents)
        {
            changes.AddRange(this.GetSelectedAgentTemplates(options_).Select(item_ => this.CreateManagedFileChange(
                analysis_,
                options_,
                manifest,
                item_.TemplatePath,
                templateVersion,
                RenderPath(item_.DestinationPath, options_),
                item_.Description)));
        }

        foreach (ClientKind client in GetSelectedClients(options_))
        {
            string clientPath = GetClientConfigPath(client);
            string templateId = this.GetTemplatePathForDestination(clientPath);
            changes.Add(this.CreateManagedFileChange(analysis_, options_, manifest, templateId, templateVersion, clientPath, $"Create configuration for {GetClientDisplayName(client)}."));
            foreach (string additionalPath in this.GetAdditionalClientConfigPaths(client))
            {
                string additionalTemplateId = this.GetTemplatePathForDestination(additionalPath);
                changes.Add(this.CreateManagedFileChange(analysis_, options_, manifest, additionalTemplateId, templateVersion, additionalPath, $"Create versionable snippet for {GetClientDisplayName(client)}."));
            }
        }

        if (options_.DryRun)
        {
            changes.Add(this.CreateWarning(".", "Dry-run mode is active."));
        }

        return changes;
    }

    public IReadOnlyList<PlannedChange> PlanConfigChanges(RepoAnalysis analysis_, BootstrapOptions options_)
    {
        List<PlannedChange> changes = [];

        if (!analysis_.Profile.Exists)
        {
            changes.Add(this.CreateWarning(analysis_.Profile.RootPath, "Target repository path does not exist."));
            return changes;
        }

        ManagedFilesManifest manifest = this._managedFilesService.Load(analysis_.Profile.RootPath);
        string templateVersion = this._managedFilesService.GetToolVersion();

        foreach (ClientKind client in GetSelectedClients(options_))
        {
            string clientPath = GetClientConfigPath(client);
            string templateId = this.GetTemplatePathForDestination(clientPath);
            changes.Add(this.CreateManagedFileChange(analysis_, options_, manifest, templateId, templateVersion, clientPath, $"Plan configuration snippet for {GetClientDisplayName(client)}."));
            foreach (string additionalPath in this.GetAdditionalClientConfigPaths(client))
            {
                string additionalTemplateId = this.GetTemplatePathForDestination(additionalPath);
                changes.Add(this.CreateManagedFileChange(analysis_, options_, manifest, additionalTemplateId, templateVersion, additionalPath, $"Plan versionable snippet for {GetClientDisplayName(client)}."));
            }
        }

        if (changes.Count == 0)
        {
            changes.Add(this.CreateWarning(".", "No clients were selected."));
        }

        return changes;
    }

    public string GetTemplatePathForDestination(string destinationPath_)
    {
        string destinationPath = destinationPath_.Replace('\\', '/');
        if (destinationPath.StartsWith("Tools/AiContextMcp/", StringComparison.OrdinalIgnoreCase)
            && destinationPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return "ai-context-mcp/{{McpProjectName}}.csproj.tpl";
        }

        TemplatePlanItem? item = GetAllTemplateItems()
            .FirstOrDefault(item_ => string.Equals(RenderStaticPath(item_.DestinationPath), destinationPath, StringComparison.OrdinalIgnoreCase));
        return item?.TemplatePath ?? string.Empty;
    }

    public static string GetCategory(string destinationPath_)
    {
        string normalized = destinationPath_.Replace('\\', '/');
        if (string.Equals(normalized, ".ai", StringComparison.OrdinalIgnoreCase))
        {
            return ".ai";
        }

        if (string.Equals(normalized, "Tools/AiContext", StringComparison.OrdinalIgnoreCase))
        {
            return "tools-ai-context";
        }

        if (string.Equals(normalized, "Tools/AiContextMcp", StringComparison.OrdinalIgnoreCase))
        {
            return "ai-context-mcp";
        }

        TemplatePlanItem? item = GetAllTemplateItems()
            .FirstOrDefault(item_ => string.Equals(RenderStaticPath(item_.DestinationPath), normalized, StringComparison.OrdinalIgnoreCase));
        return item?.Category ?? "other";
    }

    private void AddPresencePlan(List<PlannedChange> changes_, RepoAnalysis analysis_, BootstrapOptions options_, string path_, string description_, bool isFile_)
    {
        PlannedChange change = isFile_
            ? this.CreateFileChange(analysis_, options_, path_, $"Plan {description_}.")
            : this.CreateDirectoryChange(analysis_, options_, path_, $"Plan {description_}.");
        changes_.Add(change);
    }

    private PlannedChange CreateDirectoryChange(RepoAnalysis analysis_, BootstrapOptions options_, string path_, string description_)
    {
        bool exists = this._fileSystemService.DirectoryExists(analysis_.Profile.RootPath, path_);
        bool isSensitive = this._fileSystemService.IsRestrictedPath(path_);
        UpdateAction updateAction = isSensitive ? UpdateAction.Restricted : exists ? UpdateAction.Skip : UpdateAction.Create;
        ChangeType changeType = this.MapChangeType(updateAction);
        bool willWrite = updateAction == UpdateAction.Create && options_.Apply && !options_.DryRun;
        string reason = isSensitive ? "Path is restricted." : exists ? "Directory already exists." : options_.DryRun ? "Dry-run mode." : "Directory can be created.";
        GeneratedFileState state = isSensitive ? GeneratedFileState.Restricted : exists ? GeneratedFileState.GeneratedCurrent : GeneratedFileState.Missing;
        return new PlannedChange(changeType, path_, description_, willWrite, exists, false, isSensitive, reason, string.Empty, string.Empty, state, updateAction, false, !exists, string.Empty, string.Empty, false);
    }

    private PlannedChange CreateFileChange(RepoAnalysis analysis_, BootstrapOptions options_, string path_, string description_)
    {
        bool exists = this._fileSystemService.FileExists(analysis_.Profile.RootPath, path_);
        bool isSensitive = this._fileSystemService.IsRestrictedPath(path_);
        bool requiresBackup = exists && !options_.Force;
        ChangeType changeType;
        bool willWrite = false;
        string reason;

        if (isSensitive)
        {
            changeType = ChangeType.Error;
            reason = "Path is restricted.";
        }
        else if (exists && !options_.Backup && !options_.Force)
        {
            changeType = ChangeType.Skip;
            reason = "File exists and requires --backup or --force.";
        }
        else
        {
            changeType = exists ? ChangeType.Update : ChangeType.Create;
            willWrite = options_.Apply && !options_.DryRun;
            reason = options_.DryRun ? "Dry-run mode." : exists && options_.Backup ? "Backup will be created before overwrite." : exists && options_.Force ? "Overwrite allowed by --force." : "File can be created.";
        }

        return new PlannedChange(changeType, path_, description_, willWrite, exists, requiresBackup, isSensitive, reason);
    }

    private PlannedChange CreateManagedFileChange(
        RepoAnalysis analysis_,
        BootstrapOptions options_,
        ManagedFilesManifest manifest_,
        string templateId_,
        string templateVersion_,
        string path_,
        string description_)
    {
        bool exists = this._fileSystemService.FileExists(analysis_.Profile.RootPath, path_);
        bool isSensitive = this._fileSystemService.IsRestrictedPath(path_);
        string renderedContent = string.IsNullOrWhiteSpace(templateId_)
            ? string.Empty
            : this._templateService.RenderTemplate(templateId_, analysis_, options_);
        (GeneratedFileState state, UpdateAction action, string reason, string currentHash, string proposedHash, bool managedByManifest, bool hasDiff) = this._generatedFileClassifier.Classify(
            analysis_.Profile.RootPath,
            path_,
            renderedContent,
            templateId_,
            templateVersion_,
            exists,
            isSensitive,
            manifest_,
            options_);
        bool willWrite = options_.Apply && !options_.DryRun && action is UpdateAction.Create or UpdateAction.SafeUpdate or UpdateAction.AppendSection;
        bool requiresBackup = exists && willWrite && options_.Backup;
        return new PlannedChange(
            this.MapChangeType(action),
            path_,
            description_,
            willWrite,
            exists,
            requiresBackup,
            isSensitive,
            reason,
            templateId_,
            templateVersion_,
            state,
            action,
            action == UpdateAction.ManualReview,
            hasDiff,
            currentHash,
            proposedHash,
            managedByManifest);
    }

    public static IReadOnlyList<ClientKind> GetSelectedClients(BootstrapOptions options_)
    {
        if (options_.Clients.Count > 0)
        {
            return options_.Clients;
        }

        return [ClientKind.Codex, ClientKind.Vscode, ClientKind.VisualStudio];
    }

    public static string GetClientDisplayName(ClientKind client_)
    {
        return client_ switch
        {
            ClientKind.Vscode => "vscode",
            ClientKind.VisualStudio => "vs",
            ClientKind.Codex => "codex",
            ClientKind.Claude => "claude",
            ClientKind.Cursor => "cursor",
            ClientKind.Gemini => "gemini",
            _ => client_.ToString()
        };
    }

    public static string GetClientConfigPath(ClientKind client_)
    {
        return client_ switch
        {
            ClientKind.Codex => ".codex/config.toml",
            ClientKind.Vscode => ".vscode/mcp.json",
            ClientKind.VisualStudio => ".mcp.json",
            ClientKind.Claude => ".ai/client-configs/claude_desktop_config.snippet.json",
            ClientKind.Cursor => ".ai/client-configs/cursor-mcp.snippet.json",
            ClientKind.Gemini => ".ai/client-configs/gemini-mcp.snippet.json",
            _ => ".ai/client-configs/unknown.json"
        };
    }

    public IReadOnlyList<string> GetAdditionalClientConfigPaths(ClientKind client_)
    {
        if (client_ == ClientKind.Codex)
        {
            return [".ai/client-configs/codex.config.toml"];
        }

        if (client_ == ClientKind.VisualStudio)
        {
            return [".ai/client-configs/visualstudio-mcp.snippet.json"];
        }

        return [];
    }

    public static IReadOnlyList<string> GetAgentTemplateDestinationPaths()
    {
        return GetAllAgentTemplateItems().Select(item_ => RenderStaticPath(item_.DestinationPath)).ToArray();
    }

    public IReadOnlyList<string> GetAgentTemplateDestinationPaths(string profileName_)
    {
        return this.GetSelectedAgentTemplates(profileName_).Select(item_ => RenderStaticPath(item_.DestinationPath)).ToArray();
    }

    public ProfileTemplateSelection GetProfileSelection(string profileName_)
    {
        return this._profileService.GetSelection(profileName_);
    }

    private static IReadOnlyList<TemplatePlanItem> GetAllTemplateItems()
    {
        return
        [
            .. AiTemplates,
            .. ToolsAiContextTemplates,
            .. AiContextMcpTemplates,
            .. GetAllAgentTemplateItems(),
            new("client-configs/codex.config.toml.tpl", ".codex/config.toml", "client-configs", "Create configuration for Codex."),
            new("client-configs/codex.config.snippet.toml.tpl", ".ai/client-configs/codex.config.toml", "client-configs", "Create versionable Codex snippet."),
            new("client-configs/vscode.mcp.json.tpl", ".vscode/mcp.json", "client-configs", "Create configuration for Vscode."),
            new("client-configs/visualstudio.mcp.json.tpl", ".mcp.json", "client-configs", "Create configuration for vs."),
            new("client-configs/visualstudio-mcp.snippet.json.tpl", ".ai/client-configs/visualstudio-mcp.snippet.json", "client-configs", "Create configuration for vs."),
            new("client-configs/claude_desktop_config.snippet.json.tpl", ".ai/client-configs/claude_desktop_config.snippet.json", "client-configs", "Create configuration for Claude."),
            new("client-configs/cursor-mcp.snippet.json.tpl", ".ai/client-configs/cursor-mcp.snippet.json", "client-configs", "Create configuration for Cursor."),
            new("client-configs/gemini-mcp.snippet.json.tpl", ".ai/client-configs/gemini-mcp.snippet.json", "client-configs", "Create configuration for Gemini.")
        ];
    }

    private IReadOnlyList<TemplatePlanItem> GetSelectedAgentTemplates(BootstrapOptions options_)
    {
        return this.GetSelectedAgentTemplates(options_.Profile);
    }

    private IReadOnlyList<TemplatePlanItem> GetSelectedAgentTemplates(string profileName_)
    {
        ProfileTemplateSelection selection = this._profileService.GetSelection(profileName_);
        return
        [
            .. AlwaysAgentTemplates.Select(ToTemplatePlanItem),
            .. AgentTemplates.Where(item_ => selection.AgentTemplates.Contains(item_.Id, StringComparer.OrdinalIgnoreCase)).Select(ToTemplatePlanItem),
            .. InstructionTemplates.Where(item_ => selection.InstructionTemplates.Contains(item_.Id, StringComparer.OrdinalIgnoreCase)).Select(ToTemplatePlanItem),
            .. PromptTemplates.Where(item_ => selection.PromptTemplates.Contains(item_.Id, StringComparer.OrdinalIgnoreCase)).Select(ToTemplatePlanItem)
        ];
    }

    private static IReadOnlyList<TemplatePlanItem> GetAllAgentTemplateItems()
    {
        return
        [
            .. AlwaysAgentTemplates.Select(ToTemplatePlanItem),
            .. AgentTemplates.Select(ToTemplatePlanItem),
            .. InstructionTemplates.Select(ToTemplatePlanItem),
            .. PromptTemplates.Select(ToTemplatePlanItem)
        ];
    }

    private static TemplatePlanItem ToTemplatePlanItem(ProfileTemplatePlanItem item_)
    {
        return new TemplatePlanItem(item_.TemplatePath, item_.DestinationPath, item_.Category, item_.Description);
    }

    private static string RenderPath(string path_, BootstrapOptions options_)
    {
        return path_
            .Replace("{{McpProjectName}}", options_.McpProjectName, StringComparison.Ordinal)
            .Replace('\\', '/');
    }

    private static string RenderStaticPath(string path_)
    {
        return path_
            .Replace("{{McpProjectName}}", "AiRepo.ContextMcp", StringComparison.Ordinal)
            .Replace('\\', '/');
    }

    private PlannedChange CreateWarning(string path_, string description_)
    {
        return new PlannedChange(ChangeType.Warning, path_, description_, false, false, false, false, "Warning only.", string.Empty, string.Empty, GeneratedFileState.Missing, UpdateAction.Skip, false, false, string.Empty, string.Empty, false);
    }

    private PlannedChange CreateGitIgnoreChange(RepoAnalysis analysis_, BootstrapOptions options_)
    {
        string path = ".gitignore";
        bool exists = this._fileSystemService.FileExists(analysis_.Profile.RootPath, path);
        bool hasSection = false;
        if (exists)
        {
            string fullPath = this._fileSystemService.NormalizePath(analysis_.Profile.RootPath, path);
            hasSection = new GitIgnoreService().HasSection(File.ReadAllText(fullPath));
        }

        if (hasSection)
        {
            return new PlannedChange(ChangeType.Skip, path, "Ensure AiRepoKit .gitignore section.", false, true, false, false, "Section already exists.", string.Empty, string.Empty, GeneratedFileState.GeneratedCurrent, UpdateAction.Skip, false, false, string.Empty, string.Empty, false);
        }

        bool willWrite = options_.Apply && !options_.DryRun && (!exists || options_.Backup || options_.Force);
        UpdateAction updateAction = exists && !options_.Backup && !options_.Force ? UpdateAction.Skip : UpdateAction.AppendSection;
        ChangeType changeType = exists && !options_.Backup && !options_.Force ? ChangeType.Skip : exists ? ChangeType.Update : ChangeType.Create;
        string reason = options_.DryRun
            ? "Dry-run mode."
            : exists && !options_.Backup && !options_.Force
                ? "File exists and requires --backup or --force."
                : exists && options_.Backup
                    ? "Backup will be created before updating."
                    : exists && options_.Force
                    ? "Section can be appended."
                        : "File can be created.";
        GeneratedFileState state = exists ? GeneratedFileState.UnmanagedExisting : GeneratedFileState.Missing;
        return new PlannedChange(changeType, path, "Ensure AiRepoKit .gitignore section.", willWrite, exists, exists && !options_.Force, false, reason, string.Empty, string.Empty, state, updateAction, false, !hasSection, string.Empty, string.Empty, false);
    }

    private PlannedChange CreateSkip(string path_, string description_, string reason_)
    {
        return new PlannedChange(ChangeType.Skip, path_, description_, false, true, false, false, reason_, string.Empty, string.Empty, GeneratedFileState.GeneratedCurrent, UpdateAction.Skip, false, false, string.Empty, string.Empty, false);
    }

    private ChangeType MapChangeType(UpdateAction updateAction_)
    {
        return updateAction_ switch
        {
            UpdateAction.Create => ChangeType.Create,
            UpdateAction.SafeUpdate or UpdateAction.AppendSection => ChangeType.Update,
            UpdateAction.ManualReview => ChangeType.Warning,
            UpdateAction.Restricted => ChangeType.Error,
            _ => ChangeType.Skip
        };
    }

    private sealed record TemplatePlanItem(string TemplatePath, string DestinationPath, string Category, string Description);

    private sealed record ProfileTemplatePlanItem(string Id, string TemplatePath, string DestinationPath, string Category, string Description);
}
