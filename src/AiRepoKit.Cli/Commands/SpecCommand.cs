using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Commands;

public sealed class SpecCommand
{
    public CommandResult Execute(IReadOnlyList<string> arguments_)
    {
        if (arguments_.Count == 0)
        {
            return CommandResult.Ok(GetUsage());
        }

        string subcommand = arguments_[0];
        bool isHelp = string.Equals(subcommand, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcommand, "--help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(subcommand, "-h", StringComparison.OrdinalIgnoreCase);

        if (isHelp && arguments_.Count == 1)
        {
            return CommandResult.Ok(GetUsage());
        }

        string error = isHelp
            ? $"Unexpected argument(s) after Spec help: `{string.Join("`, `", arguments_.Skip(1))}`."
            : $"Unsupported Spec subcommand: `{subcommand}`.";

        return CommandResult.Failure(
            $"# Spec Command Error{Environment.NewLine}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}{GetUsage()}",
            1);
    }

    private static string GetUsage()
    {
        return """
        # Spec Command

        Usage:

        ```text
        airepo spec [help]
        ```

        This slice establishes Spec command-group routing only. No Spec lifecycle subcommand is implemented.
        """;
    }
}
