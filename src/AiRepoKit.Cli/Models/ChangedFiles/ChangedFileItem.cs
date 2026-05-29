namespace AiRepoKit.Cli.Models.ChangedFiles;

public sealed record ChangedFileItem(
    string Path,
    string Status,
    bool Staged,
    bool Unstaged,
    bool Untracked);
