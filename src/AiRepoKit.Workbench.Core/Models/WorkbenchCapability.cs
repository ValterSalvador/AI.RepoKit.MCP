namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Distinct capabilities requested by the workbench router.
/// The router requests capabilities; it never grants them.
/// </summary>
public enum WorkbenchCapability
{
    RepositoryReading,
    ProcessExecution,
    SourceModification,
    BranchWorktreeCreation,
    Commit,
    Push,
    Merge,
    ExternalMutation,
    DatabaseMigration,
    Deployment,
    ReleasePublication
}

/// <summary>
/// Extensions for workbench capability mappings and policy evaluation.
/// </summary>
public static class WorkbenchCapabilityExtensions
{
    public static string ToPolicyName(this WorkbenchCapability capability_)
    {
        return capability_ switch
        {
            WorkbenchCapability.RepositoryReading => "repo:read",
            WorkbenchCapability.ProcessExecution => "proc:exec",
            WorkbenchCapability.SourceModification => "code:edit",
            WorkbenchCapability.BranchWorktreeCreation => "git:branch",
            WorkbenchCapability.Commit => "git:commit",
            WorkbenchCapability.Push => "git:push",
            WorkbenchCapability.Merge => "git:merge",
            WorkbenchCapability.ExternalMutation => "ext:mutate",
            WorkbenchCapability.DatabaseMigration => "db:migrate",
            WorkbenchCapability.Deployment => "deploy",
            WorkbenchCapability.ReleasePublication => "release",
            _ => capability_.ToString()
        };
    }

    /// <summary>
    /// Returns the provider capability names that can satisfy this requested capability.
    /// </summary>
    public static IReadOnlyList<string> GetRequiredProviderCapabilityNames(this WorkbenchCapability capability_)
    {
        return capability_ switch
        {
            WorkbenchCapability.RepositoryReading => new[] { "repo:read", "git:read", "fs:read", "RepositoryReading" },
            WorkbenchCapability.ProcessExecution => new[] { "proc:exec", "terminal:exec", "ProcessExecution" },
            WorkbenchCapability.SourceModification => new[] { "code:edit", "fs:write", "SourceModification" },
            WorkbenchCapability.BranchWorktreeCreation => new[] { "git:branch", "BranchWorktreeCreation" },
            WorkbenchCapability.Commit => new[] { "git:commit", "Commit" },
            WorkbenchCapability.Push => new[] { "git:push", "Push" },
            WorkbenchCapability.Merge => new[] { "git:merge", "Merge" },
            WorkbenchCapability.ExternalMutation => new[] { "ext:mutate", "ExternalMutation" },
            WorkbenchCapability.DatabaseMigration => new[] { "db:migrate", "DatabaseMigration" },
            WorkbenchCapability.Deployment => new[] { "deploy", "Deployment" },
            WorkbenchCapability.ReleasePublication => new[] { "release", "ReleasePublication" },
            _ => new[] { capability_.ToString() }
        };
    }
}
