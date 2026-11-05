namespace AiRepoKit.Workbench.Core.Models;

/// <summary>
/// Classified scope of change used for policy and risk resolution.
/// </summary>
public enum ChangeScope
{
    Unknown,
    DocumentationOnly,
    LocalizedBugFix,
    PublicApiChange,
    DatabaseSchemaChange,
    AuthenticationOrAuthorizationChange,
    InfrastructureDeployment,
    GeneralFeature
}
