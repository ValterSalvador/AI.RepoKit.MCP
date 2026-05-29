using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services.Profiles;

namespace AiRepoKit.Cli.Services;

public sealed class CommandDefaultsResolver
{
    public ResolvedDefaults Resolve(BootstrapOptions options_)
    {
        RepoDetection detection = new RepoDetector().Detect(options_.RepoPath);
        string profile = options_.ProfileExplicit
            ? new ProfileService().NormalizeProfileName(options_.Profile)
            : detection.RecommendedProfile;
        bool includeMcp = options_.IncludeMcp || IsZeroConfigCommand(options_.Command);
        bool includeAgents = options_.IncludeAgents || string.Equals(options_.Command, "setup", StringComparison.OrdinalIgnoreCase);
        IReadOnlyList<ClientKind> clients = options_.Clients.Count > 0
            ? options_.Clients
            : [ClientKind.Codex, ClientKind.Vscode, ClientKind.VisualStudio];
        List<string> messages = [];
        messages.Add($"repo={detection.RepoRoot}");
        messages.Add(options_.ProfileExplicit ? $"profile={profile} explicit" : $"profile={profile} auto");
        messages.Add(options_.Clients.Count > 0 ? "clients=explicit" : "clients=codex,vscode,vs inferred");
        if (!string.IsNullOrWhiteSpace(detection.Warning) && !options_.ProfileExplicit)
        {
            messages.Add(detection.Warning);
        }

        return new ResolvedDefaults(detection, profile, clients, includeMcp, includeAgents, string.Join("; ", messages));
    }

    private static bool IsZeroConfigCommand(string command_)
    {
        return command_.Equals("setup", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ResolvedDefaults(
    RepoDetection Detection,
    string Profile,
    IReadOnlyList<ClientKind> Clients,
    bool IncludeMcp,
    bool IncludeAgents,
    string Summary);
