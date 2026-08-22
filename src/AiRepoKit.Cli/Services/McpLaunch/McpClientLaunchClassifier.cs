namespace AiRepoKit.Cli.Services.McpLaunch;

internal enum McpClientLaunchKind
{
    Portable,
    Legacy,
    Invalid
}

internal sealed record McpClientLaunchClassification(
    McpClientLaunchKind Kind,
    bool IsValid,
    string Reason,
    string? MigrationHint);

internal static class McpClientLaunchClassifier
{
    internal static McpClientLaunchClassification Classify(
        string? command_,
        IEnumerable<string>? arguments_)
    {
        string command = (command_ ?? string.Empty).Trim();
        string[] arguments = (arguments_ ?? [])
            .Where(argument_ => !string.IsNullOrWhiteSpace(argument_))
            .Select(argument_ => argument_.Trim())
            .ToArray();

        if (string.IsNullOrWhiteSpace(command))
        {
            return new McpClientLaunchClassification(
                McpClientLaunchKind.Invalid,
                false,
                "MCP launch command is missing.",
                "Use 'airepo mcp serve --repo <repo>'.");
        }

        string commandName = Path.GetFileName(command);
        bool isDotnetCommand =
            string.Equals(
                commandName,
                "dotnet",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                commandName,
                "dotnet.exe",
                StringComparison.OrdinalIgnoreCase);

        if (isDotnetCommand)
        {
            bool hasLegacyDll = arguments.Any(
                argument_ =>
                    argument_.EndsWith(
                        "AiRepo.ContextMcp.dll",
                        StringComparison.OrdinalIgnoreCase));

            bool legacyHasRepoArgument = arguments.Any(
                argument_ =>
                    string.Equals(
                        argument_,
                        "--repo",
                        StringComparison.OrdinalIgnoreCase));

            if (hasLegacyDll && legacyHasRepoArgument)
            {
                return new McpClientLaunchClassification(
                    McpClientLaunchKind.Legacy,
                    true,
                    "Legacy MCP launch is structurally valid but deprecated.",
                    "Use 'airepo mcp serve --repo <repo>' to migrate to the portable runtime.");
            }

            return new McpClientLaunchClassification(
                McpClientLaunchKind.Invalid,
                false,
                "Legacy MCP launch is malformed; expected dotnet <AiRepo.ContextMcp.dll> --repo <repo>.",
                "Use 'airepo mcp serve --repo <repo>' instead.");
        }

        bool hasMcpKeyword = arguments.Any(
            argument_ =>
                string.Equals(
                    argument_,
                    "mcp",
                    StringComparison.OrdinalIgnoreCase));

        bool hasServeKeyword = arguments.Any(
            argument_ =>
                string.Equals(
                    argument_,
                    "serve",
                    StringComparison.OrdinalIgnoreCase));

        bool portableHasRepoArgument = arguments.Any(
            argument_ =>
                string.Equals(
                    argument_,
                    "--repo",
                    StringComparison.OrdinalIgnoreCase));

        if (hasMcpKeyword &&
            hasServeKeyword &&
            portableHasRepoArgument)
        {
            return new McpClientLaunchClassification(
                McpClientLaunchKind.Portable,
                true,
                "Portable MCP launch is structurally valid and preferred.",
                null);
        }

        return new McpClientLaunchClassification(
            McpClientLaunchKind.Invalid,
            false,
            "Portable MCP launch is malformed; expected '<command> mcp serve --repo <repo>'.",
            "Use 'airepo mcp serve --repo <repo>' to start the portable runtime.");
    }
}
