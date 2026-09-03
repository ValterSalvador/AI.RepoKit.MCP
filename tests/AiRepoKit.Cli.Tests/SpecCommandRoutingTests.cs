using AiRepoKit.Cli.Commands;
using AiRepoKit.Cli.Models;
using AiRepoKit.Cli.Services;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class SpecCommandRoutingTests
{
    [Fact]
    public void Execute_EmptyArguments_ReturnsSuccessfulBoundedUsage()
    {
        CommandResult result = new SpecCommand().Execute([]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("airepo spec [help]", result.Markdown);
        Assert.Contains("routing only", result.Markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No Spec lifecycle subcommand is implemented", result.Markdown);
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("HeLp")]
    [InlineData("--HELP")]
    [InlineData("-H")]
    public void Execute_HelpToken_ReturnsSuccessfulUsage(string helpToken_)
    {
        CommandResult result = new SpecCommand().Execute([helpToken_]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("airepo spec [help]", result.Markdown);
    }

    [Fact]
    public void Execute_UnknownSubcommand_ReturnsFailureNamingSubcommand()
    {
        CommandResult result = new SpecCommand().Execute(["unknown"]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unknown", result.Markdown);
        Assert.Contains("airepo spec [help]", result.Markdown);
    }

    [Fact]
    public void Execute_ExtraArgumentsAfterHelp_AreRejected()
    {
        CommandResult result = new SpecCommand().Execute(["help", "extra"]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unexpected argument", result.Markdown);
        Assert.Contains("extra", result.Markdown);
    }

    [Fact]
    public void Execute_Plan_IsRejectedAsSpecSubcommand()
    {
        CommandResult result = new SpecCommand().Execute(["plan"]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unsupported Spec subcommand: `plan`", result.Markdown);
    }

    [Fact]
    public void ProgramRouteSpec_ReachesSpecCommand()
    {
        CommandResult result = Program.RouteSpec(["spec", "help"]);

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("# Spec Command", result.Markdown);
    }

    [Fact]
    public void ProgramRouteSpec_PlanDoesNotRouteToTopLevelPlan()
    {
        CommandResult result = Program.RouteSpec(["spec", "plan"]);

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unsupported Spec subcommand: `plan`", result.Markdown);
    }

    [Fact]
    public void ProgramParse_TopLevelPlanRemainsUnchanged()
    {
        string expectedRepoPath =
            new RepoPathResolver().Resolve(
                null,
                "plan");

        BootstrapOptions options =
            Program.Parse(
                ["plan"]);

        Assert.Equal(
            "plan",
            options.Command);
        Assert.Equal(
            expectedRepoPath,
            options.RepoPath);
        Assert.Empty(
            options.UnknownOptions);
    }

    [Fact]
    public void ProgramParse_UnrelatedCommandRemainsUnchanged()
    {
        BootstrapOptions options = Program.Parse(["doctor", "--summary"]);

        Assert.Equal("doctor", options.Command);
        Assert.True(options.Summary);
        Assert.Empty(options.UnknownOptions);
    }

    [Fact]
    public void Execute_HelpAndError_DoNotMutateFilesystem()
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        string[] entriesBefore = Directory.GetFileSystemEntries(currentDirectory).OrderBy(path_ => path_).ToArray();

        _ = new SpecCommand().Execute([]);
        _ = new SpecCommand().Execute(["unknown"]);

        string[] entriesAfter = Directory.GetFileSystemEntries(currentDirectory).OrderBy(path_ => path_).ToArray();
        Assert.Equal(entriesBefore, entriesAfter);
    }
}
