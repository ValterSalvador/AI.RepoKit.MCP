namespace AiRepoKit.Cli.Models;

public sealed record ProcessResult(
    string FileName,
    string Arguments,
    string WorkingDirectory,
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public bool Success => this.ExitCode == 0;
}
