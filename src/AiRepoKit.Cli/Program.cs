using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using AiRepoKit.Cli.Services.Profiles;

namespace AiRepoKit.Cli;

public static class Program
{
    public static int Main(string[] args_)
    {
        if (args_.Length == 0)
        {
            return RunInteractive();
        }

        BootstrapOptions options = Parse(args_);
        CommandResult result;

        if (options.UnknownOptions.Count > 0)
        {
            string message = options.UnknownOptions.Count == 1 && options.UnknownOptions[0].Contains("--repo <path>", StringComparison.OrdinalIgnoreCase)
                ? options.UnknownOptions[0]
                : $"Unknown option(s): `{string.Join("`, `", options.UnknownOptions)}`";
            result = CommandResult.Failure($"# Error{Environment.NewLine}{Environment.NewLine}{message}", 1);
        }
        else
        {
            result = options.Command.ToLowerInvariant() switch
            {
                "plan" => new PlanCommand().Execute(options),
                "init" => new InitCommand().Execute(options),
                "validate" => new ValidateCommand().Execute(options),
                "configs" => new ConfigsCommand().Execute(options),
                "doctor" => new DoctorCommand().Execute(options),
                "sample" => new SampleCommand().Execute(options),
                "bootstrap" => new BootstrapCommand().Execute(options),
                "code-index" => new CodeIndexCommand().Execute(options),
                "context-pack" => new ContextPackCommand().Execute(options),
                "graph" => new GraphCommand().Execute(options),
                "impact" => new ImpactCommand().Execute(options),
                "audit" => new AuditCommand().Execute(options),
                "detect" => new DetectCommand().Execute(options),
                "setup" => new SetupCommand().Execute(options),
                "sanitize" => new SanitizeCommand().Execute(options),
                "self-check" => new SelfCheckCommand().Execute(options),
                "org" => new OrgCommand().Execute(options),
                "mcp-diagnose" or "mcp-doctor" or "diagnose-mcp" => new McpDiagnoseCommand().Execute(options),
                "efficiency" or "token-report" or "context-efficiency" => new EfficiencyCommand().Execute(options),
                "--help" or "-h" or "help" or "" => CommandResult.Ok(GetUsage()),
                "--version" or "version" => CommandResult.Ok(GetVersion()),
                _ => CommandResult.Failure(GetUsage(), 1)
            };
        }

        Console.WriteLine(result.Markdown);
        return result.ExitCode;
    }

    private static int RunInteractive()
    {
        string repoPath = DetectInteractiveRepoPath();
        int exitCode = 0;

        Console.WriteLine("AiRepoKit interactive mode");
        Console.WriteLine();
        WriteInteractiveRepoSummary(repoPath);
        Console.WriteLine($"Selected profile: {ProfileService.DefaultProfileName}");
        Console.WriteLine("Progress is shown while longer operations run.");

        try
        {
            Console.WriteLine();
            Console.WriteLine("1. doctor");
            Console.WriteLine("2. plan");
            Console.WriteLine("3. bootstrap dry-run");
            Console.WriteLine("4. bootstrap apply with backup");
            Console.WriteLine("5. validate");
            Console.WriteLine("6. explain what will be installed");
            Console.WriteLine("7. explain profiles");
            Console.WriteLine("0. exit");
            Console.WriteLine();
            Console.Write("Select an option: ");

            string? choice = Console.ReadLine();
            if (string.Equals(choice, "0", StringComparison.Ordinal))
            {
                return 0;
            }

            CommandResult result = choice switch
            {
                "1" => ExecuteInteractive(["doctor", "--repo", repoPath]),
                "2" => ExecuteInteractive(["plan", "--repo", repoPath, "--clients", "codex,vscode,vs", "--mcp", "--profile", ProfileService.DefaultProfileName]),
                "3" => ExecuteInteractive(["bootstrap", "--repo", repoPath, "--clients", "codex,vscode,vs", "--mcp", "--agents", "--profile", ProfileService.DefaultProfileName, "--dry-run"]),
                "4" => ExecuteInteractiveApply(repoPath),
                "5" => ExecuteInteractive(["validate", "--repo", repoPath]),
                "6" => CommandResult.Ok(GetInteractiveInstallExplanation()),
                "7" => CommandResult.Ok(GetInteractiveProfileExplanation()),
                _ => CommandResult.Failure("# Error" + Environment.NewLine + Environment.NewLine + "Invalid option.", 1)
            };

            Console.WriteLine();
            Console.WriteLine(result.Markdown);
            exitCode = result.ExitCode;
        }
        finally
        {
            Console.WriteLine();
            Console.Write("Press Enter to close...");
            Console.ReadLine();
        }

        return exitCode;
    }

    private static CommandResult ExecuteInteractiveApply(string repoPath_)
    {
        Console.WriteLine();
        Console.WriteLine(GetInteractiveApplySummary());
        Console.WriteLine();
        Console.Write("Type APPLY to continue (case-insensitive): ");
        string? confirmation = Console.ReadLine();
        if (!string.Equals(confirmation, "APPLY", StringComparison.OrdinalIgnoreCase))
        {
            return CommandResult.Ok("# Bootstrap Apply" + Environment.NewLine + Environment.NewLine + "Apply cancelled. No changes were written.");
        }

        return ExecuteInteractive(["bootstrap", "--repo", repoPath_, "--clients", "codex,vscode,vs", "--mcp", "--agents", "--profile", ProfileService.DefaultProfileName, "--apply", "--backup"]);
    }

    private static void WriteInteractiveRepoSummary(string repoPath_)
    {
        Console.WriteLine($"Repo candidate: {repoPath_}");
        Console.WriteLine($"Solution file (.sln or .slnx): {HasTopLevelFile(repoPath_, "*.sln") || HasTopLevelFile(repoPath_, "*.slnx")}");
        Console.WriteLine($"C# project file (.csproj): {HasProjectFile(repoPath_)}");
        Console.WriteLine($".ai exists: {Directory.Exists(Path.Combine(repoPath_, ".ai"))}");
        Console.WriteLine($"Tools/AiContextMcp exists: {Directory.Exists(Path.Combine(repoPath_, "Tools", "AiContextMcp"))}");
    }

    private static string GetInteractiveApplySummary()
    {
        return """
        Bootstrap apply with backup will:
        - create .ai/
        - create Tools/AiContext/
        - create Tools/AiContextMcp/
        - create .codex/config.toml
        - create .vscode/mcp.json
        - create .github agent, instruction, and prompt files for the generic profile
        - build MCP Release
        - run AI context scripts
        - show progress while work is running
        - not run the server
        - not run Docker
        - not run migrations
        - not run database commands
        - not read or copy secrets
        """;
    }

    private static string GetInteractiveInstallExplanation()
    {
        return """
        # What Will Be Installed

        The bootstrap flow prepares repository-local AI context files and MCP configuration.

        - .ai/ documentation, playbooks, manifest, and budget files
        - Tools/AiContext/ local diagnostic scripts
        - Tools/AiContextMcp/ a read-only stdio MCP project
        - .codex/config.toml for Codex
        - .vscode/mcp.json for VS Code
        - .ai/client-configs/ Visual Studio MCP snippet
        - optional .github agent, instruction, and prompt files with --agents
        - default profile: generic

        Dry-run remains the default. Apply requires typing APPLY in this interactive mode.

        It does not run the target server, Docker, migrations, SQL, or database commands. It does not read or copy secrets.
        """;
    }

    private static string GetInteractiveProfileExplanation()
    {
        ProfileService profileService = new();
        string names = string.Join(", ", profileService.GetSupportedProfileNames());
        return $"""
        # Profiles

        Profiles select which optional .github agent, instruction, and prompt files are generated when --agents is used.

        Supported profiles: {names}

        - generic: small default set for Ask, Plan, Implementer, Reviewer, and Test Fixer workflows.
        - dotnet: generic plus .NET security and source-generator guidance.
        - aspnet-core: dotnet plus API and ASP.NET Core guidance.
        - legacy-dotnet: dotnet plus migration guidance for older .NET projects.
        - winforms: legacy-dotnet plus WinForms guidance.
        - oracle-datalayer: dotnet plus data-access and Oracle boundary guidance.
        - demo: broad demonstration profile for safe, general-purpose repository work.
        """;
    }

    private static CommandResult ExecuteInteractive(string[] args_)
    {
        BootstrapOptions options = Parse(args_);
        if (options.UnknownOptions.Count > 0)
        {
            return CommandResult.Failure($"# Error{Environment.NewLine}{Environment.NewLine}Unknown option(s): `{string.Join("`, `", options.UnknownOptions)}`", 1);
        }

        return options.Command.ToLowerInvariant() switch
        {
            "plan" => new PlanCommand().Execute(options),
            "validate" => new ValidateCommand().Execute(options),
            "doctor" => new DoctorCommand().Execute(options),
            "bootstrap" => new BootstrapCommand().Execute(options),
            _ => CommandResult.Failure(GetUsage(), 1)
        };
    }

    private static string DetectInteractiveRepoPath()
    {
        string baseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        if (LooksLikeRepository(baseDirectory))
        {
            return baseDirectory;
        }

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            string executableDirectory = Path.GetFullPath(Path.GetDirectoryName(processPath) ?? string.Empty);
            if (LooksLikeRepository(executableDirectory))
            {
                return executableDirectory;
            }
        }

        return Path.GetFullPath(Directory.GetCurrentDirectory());
    }

    private static bool LooksLikeRepository(string path_)
    {
        if (!Directory.Exists(path_))
        {
            return false;
        }

        return Directory.Exists(Path.Combine(path_, ".git"))
            || Directory.Exists(Path.Combine(path_, ".ai"))
            || Directory.Exists(Path.Combine(path_, "src"))
            || File.Exists(Path.Combine(path_, "global.json"))
            || File.Exists(Path.Combine(path_, "README.md"))
            || Directory.EnumerateFiles(path_, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(path_, "*.slnx", SearchOption.TopDirectoryOnly).Any()
            || HasProjectFile(path_);
    }

    private static bool HasTopLevelFile(string path_, string pattern_)
    {
        return Directory.Exists(path_) && Directory.EnumerateFiles(path_, pattern_, SearchOption.TopDirectoryOnly).Any();
    }

    private static bool HasProjectFile(string path_)
    {
        if (!Directory.Exists(path_))
        {
            return false;
        }

        Stack<string> pending = new();
        pending.Push(path_);
        HashSet<string> ignored = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            "bin",
            "obj"
        };

        while (pending.Count > 0)
        {
            string current = pending.Pop();
            if (Directory.EnumerateFiles(current, "*.csproj", SearchOption.TopDirectoryOnly).Any())
            {
                return true;
            }

            foreach (string directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                if (!ignored.Contains(Path.GetFileName(directory)))
                {
                    pending.Push(directory);
                }
            }
        }

        return false;
    }

    private static BootstrapOptions Parse(string[] args_)
    {
        string command = args_.Length > 0 ? args_[0] : string.Empty;
        string orgSubcommand = string.Empty;
        string? repoPath = null;
        string rootPath = string.Empty;
        List<ClientKind> clients = [];
        List<string> unknownOptions = [];
        bool includeMcp = false;
        bool apply = false;
        bool dryRun = true;
        bool explicitDryRun = false;
        bool backup = false;
        bool force = false;
        bool forceManaged = false;
        string profile = ProfileService.DefaultProfileName;
        bool profileExplicit = false;
        string targetFramework = "net10.0";
        string mcpServerName = "ai_repo_context";
        string toolCommandName = "airepo";
        string mcpProjectName = "AiRepo.ContextMcp";
        string mcpNamespace = "AiRepo.ContextMcp";
        string mcpAssemblyName = "AiRepo.ContextMcp";
        string mcpProjectRelativePath = "Tools/AiContextMcp/AiRepo.ContextMcp.csproj";
        bool skipBuildMcp = false;
        bool skipAiContext = false;
        bool skipCodeInventory = false;
        bool skipSecurityScan = false;
        bool skipBudget = false;
        bool skipSmoke = false;
        bool skipScripts = false;
        int maxFiles = 3000;
        int maxItems = 10000;
        bool includePrivateMembers = false;
        bool noCache = false;
        bool rebuildCache = false;
        string output = ".ai/generated/inventories";
        string format = "all";
        string task = "review-risk";
        string target = string.Empty;
        int limit = 20;
        bool verbose = false;
        bool auditJson = false;
        bool includeSource = false;
        bool createAuditBaseline = false;
        bool updateAuditBaseline = false;
        bool showAuditBaseline = false;
        bool failOnAccepted = false;
        bool skipAudit = false;
        bool includeAgents = false;
        bool requireContextPacks = false;
        bool noProgress = false;
        bool refresh = false;
        bool noRefresh = false;
        string sampleQuery = "architecture services controllers data access";
        List<string> forbiddenTerms = [];
        string sanitizeTerm = string.Empty;
        string sanitizeReplacement = string.Empty;
        bool strict = false;
        int budget = 0;
        string kind = string.Empty;
        string since = string.Empty;
        bool changedFiles = false;
        int maxDepth = 3;

        int firstOptionIndex = 1;
        if (string.Equals(command, "org", StringComparison.OrdinalIgnoreCase) && args_.Length > 1 && !args_[1].StartsWith("-", StringComparison.Ordinal))
        {
            orgSubcommand = args_[1];
            firstOptionIndex = 2;
            output = string.Empty;
            format = "markdown";
        }

        if (args_.Any(arg_ => string.Equals(arg_, "--help", StringComparison.OrdinalIgnoreCase) || string.Equals(arg_, "-h", StringComparison.OrdinalIgnoreCase)))
        {
            command = "--help";
        }

        if (args_.Any(arg_ => string.Equals(arg_, "--version", StringComparison.OrdinalIgnoreCase)))
        {
            command = "--version";
        }

        for (int index = firstOptionIndex; index < args_.Length; index++)
        {
            string value = args_[index];
            if (string.Equals(value, "--root", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                rootPath = args_[++index];
                continue;
            }

            if (string.Equals(value, "--repo", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                repoPath = args_[++index];
                continue;
            }

            if (string.Equals(value, "--clients", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                clients.AddRange(ParseClients(args_[++index]));
                continue;
            }

            if (string.Equals(value, "--mcp", StringComparison.OrdinalIgnoreCase))
            {
                includeMcp = true;
                continue;
            }

            if (string.Equals(value, "--apply", StringComparison.OrdinalIgnoreCase))
            {
                apply = true;
                dryRun = false;
                continue;
            }

            if (string.Equals(value, "--dry-run", StringComparison.OrdinalIgnoreCase))
            {
                dryRun = true;
                explicitDryRun = true;
                continue;
            }

            if (string.Equals(value, "--backup", StringComparison.OrdinalIgnoreCase))
            {
                backup = true;
                continue;
            }

            if (string.Equals(value, "--force", StringComparison.OrdinalIgnoreCase))
            {
                force = true;
                continue;
            }

            if (string.Equals(value, "--force-managed", StringComparison.OrdinalIgnoreCase))
            {
                forceManaged = true;
                continue;
            }

            if (string.Equals(value, "--profile", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                profile = args_[++index];
                profileExplicit = true;
                continue;
            }

            if (string.Equals(value, "--target-framework", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                targetFramework = args_[++index];
                continue;
            }

            if (string.Equals(value, "--mcp-server-name", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                mcpServerName = args_[++index];
                continue;
            }

            if (string.Equals(value, "--tool-command-name", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                toolCommandName = args_[++index];
                continue;
            }

            if (string.Equals(value, "--mcp-project-name", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                mcpProjectName = args_[++index];
                continue;
            }

            if (string.Equals(value, "--mcp-namespace", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                mcpNamespace = args_[++index];
                continue;
            }

            if (string.Equals(value, "--mcp-assembly-name", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                mcpAssemblyName = args_[++index];
                continue;
            }

            if (string.Equals(value, "--mcp-project-relative-path", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                mcpProjectRelativePath = args_[++index];
                continue;
            }

            if (string.Equals(value, "--skip-build-mcp", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "--skip-build", StringComparison.OrdinalIgnoreCase))
            {
                skipBuildMcp = true;
                continue;
            }

            if (string.Equals(value, "--skip-ai-context", StringComparison.OrdinalIgnoreCase))
            {
                skipAiContext = true;
                continue;
            }

            if (string.Equals(value, "--skip-code-inventory", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "--skip-code-index", StringComparison.OrdinalIgnoreCase))
            {
                skipCodeInventory = true;
                continue;
            }

            if (string.Equals(value, "--skip-security-scan", StringComparison.OrdinalIgnoreCase))
            {
                skipSecurityScan = true;
                continue;
            }

            if (string.Equals(value, "--skip-budget", StringComparison.OrdinalIgnoreCase))
            {
                skipBudget = true;
                continue;
            }

            if (string.Equals(value, "--refresh", StringComparison.OrdinalIgnoreCase))
            {
                refresh = true;
                continue;
            }

            if (string.Equals(value, "--no-refresh", StringComparison.OrdinalIgnoreCase))
            {
                noRefresh = true;
                continue;
            }

            if (string.Equals(value, "--sample-query", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                sampleQuery = args_[++index];
                continue;
            }

            if (string.Equals(value, "--forbidden-term", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                forbiddenTerms.Add(args_[++index]);
                continue;
            }

            if (string.Equals(value, "--term", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                sanitizeTerm = args_[++index];
                continue;
            }

            if ((string.Equals(value, "--replacement", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(value, "--replace-with", StringComparison.OrdinalIgnoreCase))
                && index + 1 < args_.Length)
            {
                sanitizeReplacement = args_[++index];
                continue;
            }

            if (string.Equals(value, "--strict", StringComparison.OrdinalIgnoreCase))
            {
                strict = true;
                continue;
            }

            if (string.Equals(value, "--skip-smoke", StringComparison.OrdinalIgnoreCase))
            {
                skipSmoke = true;
                continue;
            }

            if (string.Equals(value, "--skip-scripts", StringComparison.OrdinalIgnoreCase))
            {
                skipScripts = true;
                continue;
            }

            if (string.Equals(value, "--max-files", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                if (int.TryParse(args_[++index], out int parsed))
                {
                    maxFiles = parsed;
                }

                continue;
            }

            if (string.Equals(value, "--max-items", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                if (int.TryParse(args_[++index], out int parsed))
                {
                    maxItems = parsed;
                }

                continue;
            }

            if (string.Equals(value, "--include-private-members", StringComparison.OrdinalIgnoreCase))
            {
                includePrivateMembers = true;
                continue;
            }

            if (string.Equals(value, "--no-cache", StringComparison.OrdinalIgnoreCase))
            {
                noCache = true;
                continue;
            }

            if (string.Equals(value, "--rebuild-cache", StringComparison.OrdinalIgnoreCase))
            {
                rebuildCache = true;
                continue;
            }

            if (string.Equals(value, "--rebuild-index", StringComparison.OrdinalIgnoreCase))
            {
                rebuildCache = true;
                continue;
            }

            if (string.Equals(value, "--output", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                output = args_[++index];
                continue;
            }

            if (string.Equals(value, "--format", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                format = args_[++index];
                continue;
            }

            if (string.Equals(value, "--task", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                task = args_[++index];
                continue;
            }

            if (string.Equals(value, "--target", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                target = args_[++index];
                continue;
            }

            if (string.Equals(value, "--limit", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                if (int.TryParse(args_[++index], out int parsed))
                {
                    limit = parsed;
                }

                continue;
            }

            if (string.Equals(value, "--verbose", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
                continue;
            }

            if (string.Equals(value, "--max-depth", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                if (int.TryParse(args_[++index], out int parsed))
                {
                    maxDepth = parsed;
                }

                continue;
            }

            if (string.Equals(value, "--budget", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                if (int.TryParse(args_[++index], out int parsed))
                {
                    budget = parsed;
                }

                continue;
            }

            if (string.Equals(value, "--kind", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                kind = args_[++index];
                continue;
            }

            if (string.Equals(value, "--since", StringComparison.OrdinalIgnoreCase) && index + 1 < args_.Length)
            {
                since = args_[++index];
                continue;
            }

            if (string.Equals(value, "--changed-files", StringComparison.OrdinalIgnoreCase))
            {
                changedFiles = true;
                continue;
            }

            if (string.Equals(value, "--no-progress", StringComparison.OrdinalIgnoreCase))
            {
                noProgress = true;
                continue;
            }

            if (string.Equals(value, "--json", StringComparison.OrdinalIgnoreCase))
            {
                auditJson = true;
                continue;
            }

            if (string.Equals(value, "--include-source", StringComparison.OrdinalIgnoreCase))
            {
                includeSource = true;
                continue;
            }

            if (string.Equals(value, "--create-baseline", StringComparison.OrdinalIgnoreCase))
            {
                createAuditBaseline = true;
                continue;
            }

            if (string.Equals(value, "--update-baseline", StringComparison.OrdinalIgnoreCase))
            {
                updateAuditBaseline = true;
                continue;
            }

            if (string.Equals(value, "--baseline", StringComparison.OrdinalIgnoreCase))
            {
                showAuditBaseline = true;
                continue;
            }

            if (string.Equals(value, "--fail-on-accepted", StringComparison.OrdinalIgnoreCase))
            {
                failOnAccepted = true;
                continue;
            }

            if (string.Equals(value, "--skip-audit", StringComparison.OrdinalIgnoreCase))
            {
                skipAudit = true;
                continue;
            }

            if (string.Equals(value, "--agents", StringComparison.OrdinalIgnoreCase))
            {
                includeAgents = true;
                continue;
            }

            if (string.Equals(value, "--context-packs", StringComparison.OrdinalIgnoreCase))
            {
                requireContextPacks = true;
                continue;
            }

            if (string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "--version", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            unknownOptions.Add(value);
        }

        if (explicitDryRun)
        {
            dryRun = true;
        }

        ProfileService profileService = new();
        profile = profileService.NormalizeProfileName(profile);
        if (profileExplicit && !profileService.IsSupported(profile))
        {
            unknownOptions.Add($"Unsupported profile `{profile}`. Supported profiles: {string.Join(", ", profileService.GetSupportedProfileNames())}");
        }

        string resolvedRepoPath;
        try
        {
            if (string.Equals(command, "org", StringComparison.OrdinalIgnoreCase))
            {
                string orgRoot = string.IsNullOrWhiteSpace(rootPath) ? Directory.GetCurrentDirectory() : rootPath;
                rootPath = Path.GetFullPath(orgRoot);
                resolvedRepoPath = rootPath;
            }
            else
            {
                resolvedRepoPath = command is "--help" or "--version" or "help" or "version" or "" ? Directory.GetCurrentDirectory() : new RepoPathResolver().Resolve(repoPath, command);
            }
        }
        catch (InvalidOperationException exception)
        {
            unknownOptions.Add(exception.Message);
            resolvedRepoPath = Directory.GetCurrentDirectory();
        }

        BootstrapOptions parsedOptions = new(command, resolvedRepoPath, clients.Distinct().ToArray(), includeMcp, apply, dryRun, backup, force, forceManaged, profile, targetFramework, mcpServerName, toolCommandName, mcpProjectName, mcpNamespace, mcpAssemblyName, mcpProjectRelativePath, skipBuildMcp, skipAiContext, skipCodeInventory, skipSecurityScan, skipBudget, skipSmoke, skipScripts, maxFiles, maxItems, includePrivateMembers, noCache, rebuildCache, output, format, verbose, auditJson, includeSource, createAuditBaseline, updateAuditBaseline, showAuditBaseline, failOnAccepted, skipAudit, includeAgents, task, target, limit, requireContextPacks, unknownOptions, noProgress, refresh, noRefresh, sampleQuery, profileExplicit, forbiddenTerms, sanitizeTerm, sanitizeReplacement, strict, string.Empty, budget, kind, since, changedFiles, rootPath, orgSubcommand, maxDepth);
        if (command is "--help" or "--version" or "help" or "version" or "")
        {
            return parsedOptions;
        }

        try
        {
            if (string.Equals(command, "org", StringComparison.OrdinalIgnoreCase))
            {
                return parsedOptions;
            }

            ResolvedDefaults resolvedDefaults = new CommandDefaultsResolver().Resolve(parsedOptions);
            return new BootstrapOptions(command, resolvedDefaults.Detection.RepoRoot, resolvedDefaults.Clients, resolvedDefaults.IncludeMcp, apply, dryRun, backup, force, forceManaged, resolvedDefaults.Profile, targetFramework, mcpServerName, toolCommandName, mcpProjectName, mcpNamespace, mcpAssemblyName, mcpProjectRelativePath, skipBuildMcp, skipAiContext, skipCodeInventory, skipSecurityScan, skipBudget, skipSmoke, skipScripts, maxFiles, maxItems, includePrivateMembers, noCache, rebuildCache, output, format, verbose, auditJson, includeSource, createAuditBaseline, updateAuditBaseline, showAuditBaseline, failOnAccepted, skipAudit, resolvedDefaults.IncludeAgents, task, target, limit, requireContextPacks, unknownOptions, noProgress, refresh, noRefresh, sampleQuery, profileExplicit, forbiddenTerms, sanitizeTerm, sanitizeReplacement, strict, resolvedDefaults.Summary, budget, kind, since, changedFiles, rootPath, orgSubcommand, maxDepth);
        }
        catch
        {
            return parsedOptions;
        }
    }

    private static IEnumerable<ClientKind> ParseClients(string value_)
    {
        foreach (string part in value_.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            ClientKind? client = part.ToLowerInvariant() switch
            {
                "codex" => ClientKind.Codex,
                "vscode" => ClientKind.Vscode,
                "vs" => ClientKind.VisualStudio,
                "visualstudio" => ClientKind.VisualStudio,
                "claude" => ClientKind.Claude,
                "cursor" => ClientKind.Cursor,
                "gemini" => ClientKind.Gemini,
                _ => null
            };

            if (client.HasValue)
            {
                yield return client.Value;
            }
        }
    }

    private static string GetUsage()
    {
        return """
        # AiRepoKit.Cli

        Usage:

        ```text
        airepo bootstrap --repo <path> --clients codex,vscode,vs [--mcp] [--agents] [--profile generic] [--apply] [--backup|--force|--force-managed]
        airepo setup [--repo <path>] [--apply] [--profile name] [--clients codex,vscode,vs] [--strict]
        airepo detect [--repo <path>] [--json]
        airepo sanitize [--repo <path>] --term <term> --replacement <value> [--apply --backup]
        airepo init --repo <path> --clients codex,vscode,vs --mcp [--agents] [--profile generic] [--apply] [--backup|--force|--force-managed]
        airepo plan --repo <path> [--clients codex,vscode,vs] [--mcp] [--agents] [--profile generic]
        airepo code-index [--repo <path>] [--apply] [--max-files 3000] [--max-items 10000] [--include-private-members] [--format json|markdown|all] [--no-cache|--rebuild-cache|--rebuild-index]
        airepo context-pack [--repo <path>] [--task change-api|change-ui|fix-build|update-package|review-risk|security-review|test-generation|changed-files] [--target name] [--apply] [--format json|markdown|all] [--limit 20] [--budget 12000] [--skip-code-index|--rebuild-index]
        airepo graph [--repo <path>] [--kind project|symbol|risk] [--format json|markdown|all] [--apply] [--limit 20] [--budget 12000]
        airepo impact [--repo <path>] [--changed-files] [--target name] [--since origin/main] [--format json|markdown|all] [--apply] [--limit 20] [--budget 12000]
        airepo audit [--repo <path>] [--include-source] [--create-baseline] [--update-baseline] [--baseline] [--fail-on-accepted] [--json] [--verbose]
        airepo self-check [--repo <path>] [--agents] [--context-packs] [--fail-on-accepted] [--skip-audit] [--skip-build-mcp] [--skip-code-index] [--skip-budget] [--json] [--verbose]
        airepo mcp-diagnose [--repo <path>] [--clients codex,vscode,vs] [--skip-build] [--skip-smoke] [--skip-budget] [--json] [--verbose]
        airepo efficiency [--repo <path>] [--profile generic] [--sample-query "architecture services controllers data access"] [--json] [--no-progress] [--verbose] [--refresh|--no-refresh] [--rebuild-index] [--skip-budget]
        airepo org scan [--root <path>] [--max-depth 3] [--json|--format markdown|json|csv] [--output <path>] [--apply] [--no-progress]
        airepo org report [--root <path>] [--max-depth 3] [--json|--format markdown|json|csv] [--apply] [--no-progress]
        airepo org self-check [--root <path>] [--max-depth 3] [--json|--format markdown|json|csv] [--no-progress]
        airepo org setup [--root <path>] [--max-depth 3] [--dry-run] [--json|--format markdown|json|csv] [--no-progress]
        airepo org efficiency [--root <path>] [--max-depth 3] [--json|--format markdown|json|csv] [--apply] [--no-progress]
        airepo doctor --repo <path> [--target-framework net10.0] [--profile generic] [--dry-run|--apply]
        airepo validate --repo <path>
        airepo sample --repo <path> [--apply] [--force]
        airepo configs --repo <path> --clients codex,vscode,vs,claude,cursor,gemini
        airepo --help
        airepo --version
        ```

        Client names:

        ```text
        --clients codex,vscode,vs
        ```

        `vs` is the preferred Visual Studio client name. `visualstudio` remains accepted as a legacy alias for `vs`.

        Common options:

        ```text
        --repo <path>                 Target repository. If omitted, airepo resolves upward from the current directory, preferring .git.
        --dry-run                     Plan only. This is the default for init, bootstrap, and sample.
        --apply                       Write planned files.
        --backup                      Back up existing managed files before overwrite.
        --force                       Overwrite without backup when allowed.
        --force-managed               Update managed files even when local edits are detected.
        --profile <name>              Agent/profile set. If omitted, airepo auto-detects a profile and falls back to generic on low confidence.
        --mcp                         Include repository-local MCP scaffold and client config.
        --agents                      Include versionable agent, instruction, and prompt files.
        --context-packs               Require context packs during self-check.
        --forbidden-term <term>       Fail self-check when a forbidden term is found.
        --term <term>                 Term for sanitize.
        --replacement <value>         Replacement for sanitize.
        --strict                      Treat locked MCP build failures as blocking in setup.
        --json                        Emit JSON when supported by the command.
        --verbose                     Emit more detail when supported by the command.
        --no-progress                 Disable terminal progress messages and spinner.
        --budget <tokens>             Approximate context token budget using chars / 4.
        --root <path>                 Organization scan root for org commands. If omitted, current directory.
        --max-depth <number>          Max directory depth for org discovery. Default: 3.
        ```

        Progress:

        ```text
        Long-running commands write progress to stderr when running in an interactive terminal. JSON output on stdout remains parseable, and --json disables progress automatically.
        ```

        MCP/bootstrap options:

        ```text
        --target-framework <tfm>              Default: net10.0.
        --mcp-server-name <name>              Default: ai_repo_context.
        --tool-command-name <name>            Default: airepo.
        --mcp-project-name <name>
        --mcp-namespace <namespace>
        --mcp-assembly-name <name>
        --mcp-project-relative-path <path>
        --skip-build-mcp
        --skip-ai-context
        --skip-code-inventory
        --skip-code-index
        --skip-security-scan
        --skip-budget
        --skip-scripts
        --refresh
        --no-refresh
        --rebuild-index
        ```

        Audit baseline options:

        ```text
        --include-source              Include redacted source previews.
        --create-baseline             Create .ai/policies/audit-baseline.json with review-required entries.
        --update-baseline             Merge new findings into the existing baseline.
        --baseline                    Print baseline summary.
        --fail-on-accepted            Treat accepted and false-positive baseline matches as blocking.
        --skip-audit                  Skip audit in self-check.
        ```

        MCP diagnostics skip options:

        ```text
        --skip-build                  Skip Release MCP build.
        --skip-smoke                  Skip JSON-RPC initialize/tools-list smoke test.
        --skip-budget                 Skip MCP response budget script.
        ```

        Code-index cache options:

        ```text
        --max-files <number>          Default: 3000.
        --max-items <number>          Default: 10000.
        --include-private-members     Include private members in symbol inventory.
        --no-cache                    Do not read or write .ai/generated/cache/code-index-cache.json.
        --rebuild-cache               Ignore existing cache and write a fresh cache when --apply is active.
        --rebuild-index               Alias used by context-pack to refresh the index first.
        --output <path>               Default: .ai/generated/inventories.
        --format <json|markdown|all>  Default: all.
        ```

        Context-pack options:

        ```text
        --task <name>                 change-api, change-ui, fix-build, update-package, review-risk, security-review, test-generation, or changed-files.
        --target <name>               Optional task target used in pack names and selection.
        --limit <number>              Default: 20.
        ```

        Graph and impact:

        ```text
        --kind <name>                 Graph kind: project, symbol, or risk. Omit to preview all graph kinds.
        --since <ref>                 Analyze files changed since a Git ref for impact.
        --changed-files               Explicitly request changed-files impact mode.
        --budget <tokens>             Approximate output budget; reports estimatedTokens, budget, truncated, and cuts.
        ```

        Org rollout:

        ```text
        Org commands are safe by default. They do not modify child repositories unless an explicit --apply is supported and provided. In v1.3.0, org setup is dry-run only. Org self-check skips audit, MCP build, budget, and code-index by default.
        Export formats: markdown, json, csv. Use --json for parseable stdout.
        ```
        """;
    }

    private static string GetVersion()
    {
        return TemplateService.GetToolVersion();
    }
}
