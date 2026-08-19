namespace AiRepoKit.Cli.Services.McpBudget;

/// <summary>
/// Exit class for an MCP budget run.
/// Numeric contract preserved from the PowerShell script:
///   0 = completed with no budget failures
///   1 = fatal infrastructure/startup/protocol failure
///   2 = completed but one or more budget smoke validations failed
/// </summary>
public enum McpBudgetExitClass
{
    Success = 0,
    FatalFailure = 1,
    ValidationFailure = 2
}
