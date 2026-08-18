using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class BootstrapOptionsAndCliParsingTests
{
    [Fact]
    public void BootstrapOptions_StoresScriptShellPowerShell()
    {
        var options = CreateSampleOptions(ScriptShell.PowerShell);
        Assert.Equal(ScriptShell.PowerShell, options.ScriptShell);
    }

    [Fact]
    public void BootstrapOptions_StoresScriptShellBash()
    {
        var options = CreateSampleOptions(ScriptShell.Bash);
        Assert.Equal(ScriptShell.Bash, options.ScriptShell);
    }

    [Fact]
    public void BootstrapOptions_StoresScriptShellAuto()
    {
        var options = CreateSampleOptions(ScriptShell.Auto);
        Assert.Equal(ScriptShell.Auto, options.ScriptShell);
    }

    [Fact]
    public void BootstrapOptionsWith_PreservesScriptShell()
    {
        var options = CreateSampleOptions(ScriptShell.Bash);
        var modified = options.With(command_: "new-cmd");
        Assert.Equal(ScriptShell.Bash, modified.ScriptShell);
    }

    [Fact]
    public void BootstrapOptionsWith_CanOverrideScriptShell()
    {
        var options = CreateSampleOptions(ScriptShell.PowerShell);
        var modified = options.With(scriptShell_: ScriptShell.Auto);
        Assert.Equal(ScriptShell.Auto, modified.ScriptShell);
    }

    [Fact]
    public void ProgramParse_AcceptsShellPowershell()
    {
        var options = Program.Parse(["bootstrap", "--shell", "powershell"]);
        Assert.Equal(ScriptShell.PowerShell, options.ScriptShell);
        Assert.Empty(options.UnknownOptions);
    }

    [Fact]
    public void ProgramParse_AcceptsShellBash()
    {
        var options = Program.Parse(["bootstrap", "--shell", "bash"]);
        Assert.Equal(ScriptShell.Bash, options.ScriptShell);
        Assert.Empty(options.UnknownOptions);
    }

    [Fact]
    public void ProgramParse_AcceptsShellAuto()
    {
        var options = Program.Parse(["bootstrap", "--shell", "auto"]);
        Assert.Equal(ScriptShell.Auto, options.ScriptShell);
        Assert.Empty(options.UnknownOptions);
    }

    [Fact]
    public void ProgramParse_AcceptsMixedCaseValidValues()
    {
        var options1 = Program.Parse(["bootstrap", "--shell", "PoWeRsHeLl"]);
        Assert.Equal(ScriptShell.PowerShell, options1.ScriptShell);

        var options2 = Program.Parse(["bootstrap", "--shell", "BASH"]);
        Assert.Equal(ScriptShell.Bash, options2.ScriptShell);

        var options3 = Program.Parse(["bootstrap", "--shell", "AuTo"]);
        Assert.Equal(ScriptShell.Auto, options3.ScriptShell);
    }

    [Fact]
    public void ProgramParse_ExplicitShellOverridesAirepoShellEnv()
    {
        var env = new TestEnvironmentAccessor().Set("AIREPO_SHELL", "bash");
        var options = Program.Parse(["bootstrap", "--shell", "powershell"], env);
        Assert.Equal(ScriptShell.PowerShell, options.ScriptShell);
    }

    [Fact]
    public void ProgramParse_NoExplicitValueUsesAirepoShellEnv()
    {
        var env = new TestEnvironmentAccessor().Set("AIREPO_SHELL", "bash");
        var options = Program.Parse(["bootstrap"], env);
        Assert.Equal(ScriptShell.Bash, options.ScriptShell);
    }

    [Fact]
    public void ProgramParse_NoExplicitAndNoEnvUsesPowerShellDefault()
    {
        var env = new TestEnvironmentAccessor();
        var options = Program.Parse(["bootstrap"], env);
        Assert.Equal(ScriptShell.PowerShell, options.ScriptShell);
    }

    [Fact]
    public void ProgramParse_InvalidExplicitShellProducesNormalCliValidationFailure()
    {
        var options = Program.Parse(["bootstrap", "--shell", "invalid_shell"]);
        Assert.NotEmpty(options.UnknownOptions);
        Assert.Contains(options.UnknownOptions, opt => opt.Contains("invalid_shell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgramParse_InvalidAirepoShellEnvProducesNormalCliValidationFailure()
    {
        var env = new TestEnvironmentAccessor().Set("AIREPO_SHELL", "invalid_shell");
        var options = Program.Parse(["bootstrap"], env);
        Assert.NotEmpty(options.UnknownOptions);
        Assert.Contains(options.UnknownOptions, opt => opt.Contains("invalid_shell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgramParse_PwshIsRejected()
    {
        var options = Program.Parse(["bootstrap", "--shell", "pwsh"]);
        Assert.NotEmpty(options.UnknownOptions);
        Assert.Contains(options.UnknownOptions, opt => opt.Contains("pwsh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgramParse_ZshIsRejected()
    {
        var options = Program.Parse(["bootstrap", "--shell", "zsh"]);
        Assert.NotEmpty(options.UnknownOptions);
        Assert.Contains(options.UnknownOptions, opt => opt.Contains("zsh", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgramParse_MissingValueAfterShellProducesNormalCliError()
    {
        var options = Program.Parse(["bootstrap", "--shell"]);
        Assert.NotEmpty(options.UnknownOptions);
        Assert.Contains(options.UnknownOptions, opt => opt.Equals("--shell", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgramParse_ShellFollowedBySummary_DoesNotConsumeSummaryAndParsesSummary()
    {
        var options = Program.Parse(["bootstrap", "--shell", "--summary"]);
        Assert.Contains(options.UnknownOptions, opt => opt.Equals("--shell", StringComparison.OrdinalIgnoreCase));
        Assert.True(options.Summary);
        Assert.DoesNotContain(options.UnknownOptions, opt => opt.Contains("--summary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProgramParse_ShellFollowedByQuick_DoesNotConsumeQuickAndParsesQuick()
    {
        var options = Program.Parse(["bootstrap", "--shell", "--quick"]);
        Assert.Contains(options.UnknownOptions, opt => opt.Equals("--shell", StringComparison.OrdinalIgnoreCase));
        Assert.True(options.Quick);
        Assert.DoesNotContain(options.UnknownOptions, opt => opt.Contains("--quick", StringComparison.OrdinalIgnoreCase));
    }

    private static BootstrapOptions CreateSampleOptions(ScriptShell shell)
    {
        return new BootstrapOptions(
            command_: "bootstrap",
            repoPath_: ".",
            clients_: [],
            includeMcp_: false,
            apply_: false,
            dryRun_: true,
            backup_: false,
            force_: false,
            forceManaged_: false,
            profile_: "generic",
            targetFramework_: "net10.0",
            mcpServerName_: "ai_repo_context",
            toolCommandName_: "airepo",
            mcpProjectName_: "AiRepo.ContextMcp",
            mcpNamespace_: "AiRepo.ContextMcp",
            mcpAssemblyName_: "AiRepo.ContextMcp",
            mcpProjectRelativePath_: "Tools/AiContextMcp/AiRepo.ContextMcp.csproj",
            skipBuildMcp_: false,
            skipAiContext_: false,
            skipCodeInventory_: false,
            skipSecurityScan_: false,
            skipBudget_: false,
            skipSmoke_: false,
            skipScripts_: false,
            maxFiles_: 3000,
            maxItems_: 10000,
            includePrivateMembers_: false,
            noCache_: false,
            rebuildCache_: false,
            output_: ".ai/generated/inventories",
            format_: "all",
            verbose_: false,
            summary_: false,
            auditJson_: false,
            timings_: false,
            includeSource_: false,
            createAuditBaseline_: false,
            updateAuditBaseline_: false,
            showAuditBaseline_: false,
            failOnAccepted_: false,
            skipAudit_: false,
            includeAgents_: false,
            task_: "review-risk",
            target_: "",
            limit_: 20,
            requireContextPacks_: false,
            unknownOptions_: [],
            scriptShell_: shell);
    }

    private sealed class TestEnvironmentAccessor : IEnvironmentAccessor
    {
        private readonly Dictionary<string, string?> _variables = new(StringComparer.OrdinalIgnoreCase);

        public TestEnvironmentAccessor Set(string name, string? value)
        {
            _variables[name] = value;
            return this;
        }

        public string? GetEnvironmentVariable(string name)
        {
            return _variables.TryGetValue(name, out string? value) ? value : null;
        }
    }
}
