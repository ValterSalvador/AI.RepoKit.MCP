namespace AiRepoKit.Cli.Models;

public sealed record CommandResult
{
    public CommandResult(bool success_, string markdown_, int exitCode_)
    {
        this.Success = success_;
        this.Markdown = markdown_;
        this.ExitCode = exitCode_;
    }

    public bool Success { get; }

    public string Markdown { get; }

    public int ExitCode { get; }

    public static CommandResult Ok(string markdown_) => new(true, markdown_, 0);

    public static CommandResult Failure(string markdown_, int exitCode_) => new(false, markdown_, exitCode_);
}
