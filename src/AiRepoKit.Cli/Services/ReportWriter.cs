using System.Text;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Models.ManagedFiles;
using System.Reflection;

namespace AiRepoKit.Cli.Services;

public sealed class ReportWriter
{
    public string WritePlan(RepoAnalysis analysis_, IReadOnlyList<PlannedChange> changes_, string title_, BootstrapOptions options_)
    {
        StringBuilder builder = new();
        builder.AppendLine($"# {title_}");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{analysis_.Profile.RootPath}`");
        builder.AppendLine($"- Exists: `{analysis_.Profile.Exists}`");
        builder.AppendLine($"- Solution files at root: `{analysis_.SolutionFiles.Count}`");
        builder.AppendLine($"- C# project files: `{analysis_.ProjectFiles.Count}`");
        builder.AppendLine($"- global.json: `{analysis_.HasGlobalJson}`");
        builder.AppendLine($"- Detected languages: `{FormatList(analysis_.DetectedLanguages)}`");
        builder.AppendLine($"- Repository types: `{FormatList(analysis_.RepositoryTypes)}`");
        builder.AppendLine($"- Repository signals: `{FormatList(analysis_.RepositorySignals)}`");
        builder.AppendLine($"- Profile: `{options_.Profile}`");
        builder.AppendLine($"- Selected clients: `{string.Join(", ", ConfigGenerator.GetSelectedClients(options_).Select(ConfigGenerator.GetClientDisplayName))}`");
        builder.AppendLine();
        builder.AppendLine("## Existing Infrastructure");
        builder.AppendLine();
        builder.AppendLine($"- .ai: `{analysis_.HasAiDirectory}`");
        builder.AppendLine($"- Tools/AiContext: `{analysis_.HasToolsAiContext}`");
        builder.AppendLine($"- Tools/AiContextMcp: `{analysis_.HasToolsAiContextMcp}`");
        builder.AppendLine($"- .codex/config.toml: `{analysis_.HasCodexConfig}`");
        builder.AppendLine($"- .vscode/mcp.json: `{analysis_.HasVsCodeMcpConfig}`");
        builder.AppendLine();
        builder.AppendLine("## Planned Changes");
        builder.AppendLine();
        builder.AppendLine("| Type | Action | State | Path | WillWrite | Managed | Diff | Backup | Description | Reason |");
        builder.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        foreach (PlannedChange change in changes_)
        {
            builder.AppendLine($"| {change.ChangeType} | `{change.UpdateAction}` | `{change.GeneratedFileState}` | `{change.Path}` | `{change.WillWrite}` | `{change.ManagedByManifest}` | `{change.HasDiff}` | `{change.RequiresBackup}` | {change.Description} | {change.Reason} |");
        }

        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine($"- Creates: `{changes_.Count(change_ => change_.UpdateAction == UpdateAction.Create)}`");
        builder.AppendLine($"- SafeUpdates: `{changes_.Count(change_ => change_.UpdateAction == UpdateAction.SafeUpdate)}`");
        builder.AppendLine($"- ManualReviews: `{changes_.Count(change_ => change_.UpdateAction == UpdateAction.ManualReview)}`");
        builder.AppendLine($"- Skips: `{changes_.Count(change_ => change_.UpdateAction == UpdateAction.Skip)}`");
        builder.AppendLine($"- Restricted: `{changes_.Count(change_ => change_.UpdateAction == UpdateAction.Restricted)}`");
        builder.AppendLine($"- AppendSections: `{changes_.Count(change_ => change_.UpdateAction == UpdateAction.AppendSection)}`");
        builder.AppendLine($"- Warnings: `{changes_.Count(change_ => change_.ChangeType == ChangeType.Warning)}`");
        builder.AppendLine($"- Errors: `{changes_.Count(change_ => change_.ChangeType == ChangeType.Error)}`");
        builder.AppendLine();
        builder.AppendLine("## Manual Review");
        builder.AppendLine();
        IReadOnlyList<PlannedChange> manualReviews = changes_.Where(change_ => change_.RequiresManualReview).ToArray();
        if (manualReviews.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (PlannedChange change in manualReviews.OrderBy(change_ => change_.Path, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- `{change.Path}` state `{change.GeneratedFileState}` reason `{change.Reason}`");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Category Counts");
        builder.AppendLine();
        foreach (IGrouping<string, PlannedChange> category in changes_
                     .Where(change_ => change_.UpdateAction is UpdateAction.Create or UpdateAction.SafeUpdate or UpdateAction.Skip or UpdateAction.ManualReview or UpdateAction.AppendSection)
                     .GroupBy(change_ => ConfigGenerator.GetCategory(change_.Path))
                     .OrderBy(group_ => group_.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"- {category.Key}: `{category.Count()}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Diff Signals");
        builder.AppendLine();
        IReadOnlyList<PlannedChange> diffChanges = changes_.Where(change_ => change_.HasDiff && !string.IsNullOrWhiteSpace(change_.ProposedHash)).ToArray();
        if (diffChanges.Count == 0)
        {
            builder.AppendLine("- None");
        }
        else
        {
            foreach (PlannedChange change in diffChanges.OrderBy(change_ => change_.Path, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine($"- `{change.Path}` action `{change.UpdateAction}` proposed `{ShortHash(change.ProposedHash)}` current `{ShortHash(change.CurrentHash)}`");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public string WriteValidation(RepoAnalysis analysis_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Validation");
        builder.AppendLine();
        builder.AppendLine($"- Repo: `{analysis_.Profile.RootPath}`");
        builder.AppendLine($"- Exists: `{analysis_.Profile.Exists}`");
        builder.AppendLine();
        builder.AppendLine("## Status");
        builder.AppendLine();
        builder.AppendLine($"- Detected languages: `{FormatList(analysis_.DetectedLanguages)}`");
        builder.AppendLine($"- Repository types: `{FormatList(analysis_.RepositoryTypes)}`");
        builder.AppendLine($"- Repository signals: `{FormatList(analysis_.RepositorySignals)}`");
        builder.AppendLine($"- .ai/README.md: `{FileExists(analysis_, ".ai/README.md")}`");
        builder.AppendLine($"- .ai manifest: `{HasMcpManifest(analysis_)}`");
        builder.AppendLine($"- Tools/AiContext/UpdateAiContext.ps1: `{FileExists(analysis_, "Tools/AiContext/UpdateAiContext.ps1")}`");
        builder.AppendLine($"- Tools/AiContext/CheckSdkAlignment.ps1: `{FileExists(analysis_, "Tools/AiContext/CheckSdkAlignment.ps1")}`");
        builder.AppendLine($"- Tools/AiContext/UpdateCodeInventory.ps1: `{FileExists(analysis_, "Tools/AiContext/UpdateCodeInventory.ps1")}`");
        builder.AppendLine($"- Tools/AiContext/InvokeBuildDiagnostics.ps1: `{FileExists(analysis_, "Tools/AiContext/InvokeBuildDiagnostics.ps1")}`");
        builder.AppendLine($"- Tools/AiContext/CheckSecrets.ps1: `{FileExists(analysis_, "Tools/AiContext/CheckSecrets.ps1")}`");
        builder.AppendLine($"- Tools/AiContext/MeasureMcpResponseBudget.ps1: `{FileExists(analysis_, "Tools/AiContext/MeasureMcpResponseBudget.ps1")}`");
        builder.AppendLine($"- .ai/generated/inventories/symbol-inventory.json: `{FileExists(analysis_, ".ai/generated/inventories/symbol-inventory.json")}`");
        builder.AppendLine($"- .ai/generated/inventories/endpoint-inventory.json: `{FileExists(analysis_, ".ai/generated/inventories/endpoint-inventory.json")}`");
        builder.AppendLine($"- Tools/AiContextMcp: `{DirectoryExists(analysis_, "Tools/AiContextMcp")}`");
        builder.AppendLine($"- Tools/AiContextMcp/*.csproj: `{HasMcpProject(analysis_)}`");
        builder.AppendLine($"- .codex/config.toml: `{analysis_.HasCodexConfig}`");
        builder.AppendLine($"- .vscode/mcp.json: `{analysis_.HasVsCodeMcpConfig}`");
        builder.AppendLine($"- .ai/client-configs: `{HasClientConfigs(analysis_)}`");
        return builder.ToString().TrimEnd();
    }

    public string WriteDoctor(
        RepoAnalysis analysis_,
        BootstrapOptions options_,
        GitService gitService_,
        FileSystemService fileSystemService_,
        string templatesRoot_,
        int templatesFound_,
        bool templatesAvailable_,
        string templatesError_)
    {
        StringBuilder builder = new();
        builder.AppendLine("# Doctor");
        builder.AppendLine();
        builder.AppendLine($"- .NET SDK version: `{fileSystemService_.GetDotNetSdkVersion()}`");
        builder.AppendLine($"- .NET 10 SDK installed: `{fileSystemService_.HasDotNet10Sdk()}`");
        builder.AppendLine($"- git available: `{gitService_.IsGitAvailable()}`");
        builder.AppendLine($"- OS: `{fileSystemService_.GetOperatingSystem()}`");
        builder.AppendLine($"- Repo: `{analysis_.Profile.RootPath}`");
        builder.AppendLine($"- Repo exists: `{analysis_.Profile.Exists}`");
        builder.AppendLine($"- Target framework: `{options_.TargetFramework}`");
        builder.AppendLine($"- Profile: `{options_.Profile}`");
        builder.AppendLine($"- Selected clients: `{string.Join(", ", ConfigGenerator.GetSelectedClients(options_).Select(ConfigGenerator.GetClientDisplayName))}`");
        builder.AppendLine($"- Mode: `{(options_.Apply && !options_.DryRun ? "apply" : "dry-run")}`");
        builder.AppendLine($"- TemplatesRoot: `{templatesRoot_}`");
        builder.AppendLine($"- TemplatesFound: `{templatesFound_}`");
        builder.AppendLine($"- TemplatesAvailable: `{templatesAvailable_}`");
        builder.AppendLine($"- Essential .ai templates: `{TemplateExists(templatesRoot_, "ai/README.md.tpl") && TemplateExists(templatesRoot_, "ai/manifests/mcp-context-manifest.json.tpl")}`");
        builder.AppendLine($"- Essential tools-ai-context templates: `{TemplateExists(templatesRoot_, "tools-ai-context/UpdateAiContext.ps1.tpl") && TemplateExists(templatesRoot_, "tools-ai-context/UpdateCodeInventory.ps1.tpl") && TemplateExists(templatesRoot_, "tools-ai-context/CheckSecrets.ps1.tpl")}`");
        builder.AppendLine($"- Essential ai-context-mcp templates: `{TemplateExists(templatesRoot_, "ai-context-mcp/Program.cs.tpl") && TemplateExists(templatesRoot_, "ai-context-mcp/Tools/RepositoryContextTools.cs.tpl")}`");
        builder.AppendLine($"- Essential client-configs templates: `{TemplateExists(templatesRoot_, "client-configs/codex.config.toml.tpl") && TemplateExists(templatesRoot_, "client-configs/vscode.mcp.json.tpl")}`");
        if (!string.IsNullOrWhiteSpace(templatesError_))
        {
            builder.AppendLine($"- TemplatesError: `{templatesError_}`");
        }

        return builder.ToString().TrimEnd();
    }

    private static bool HasMcpManifest(RepoAnalysis analysis_)
    {
        if (!analysis_.Profile.Exists)
        {
            return false;
        }

        string rootPath = analysis_.Profile.RootPath;
        return File.Exists(Path.Combine(rootPath, ".ai", "manifests", "mcp-context-manifest.json"))
            || File.Exists(Path.Combine(rootPath, ".ai", "mcp-context-manifest.json"));
    }

    private static bool FileExists(RepoAnalysis analysis_, string relativePath_)
    {
        return analysis_.Profile.Exists && File.Exists(Path.Combine(analysis_.Profile.RootPath, relativePath_.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool DirectoryExists(RepoAnalysis analysis_, string relativePath_)
    {
        return analysis_.Profile.Exists && Directory.Exists(Path.Combine(analysis_.Profile.RootPath, relativePath_.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool HasMcpProject(RepoAnalysis analysis_)
    {
        return analysis_.Profile.Exists
            && Directory.Exists(Path.Combine(analysis_.Profile.RootPath, "Tools", "AiContextMcp"))
            && Directory.EnumerateFiles(Path.Combine(analysis_.Profile.RootPath, "Tools", "AiContextMcp"), "*.csproj", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool TemplateExists(string templatesRoot_, string relativePath_)
    {
        if (string.Equals(templatesRoot_, "embedded://Templates/", StringComparison.OrdinalIgnoreCase))
        {
            string resourceName = "Templates/" + relativePath_.Replace('\\', '/');
            return Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .Select(name_ => name_.Replace('\\', '/'))
                .Contains(resourceName, StringComparer.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(templatesRoot_)
            && File.Exists(Path.Combine(templatesRoot_, relativePath_.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static bool HasClientConfigs(RepoAnalysis analysis_)
    {
        if (!analysis_.Profile.Exists)
        {
            return false;
        }

        return Directory.Exists(Path.Combine(analysis_.Profile.RootPath, ".ai", "client-configs"));
    }

    private static string ShortHash(string hash_)
    {
        return string.IsNullOrWhiteSpace(hash_) ? "-" : hash_[..Math.Min(12, hash_.Length)];
    }

    private static string FormatList(IReadOnlyList<string> values_)
    {
        return values_.Count == 0 ? "none" : string.Join(", ", values_);
    }
}
