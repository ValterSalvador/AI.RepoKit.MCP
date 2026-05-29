namespace AiRepoKit.Cli.Models.ChangedFiles;

public sealed record ChangedFilesResult(
    string GeneratedAtLocal,
    string RepoRoot,
    string Since,
    bool GitAvailable,
    IReadOnlyList<ChangedFileItem> Files,
    IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<string> StagedFiles => this.Files.Where(file_ => file_.Staged).Select(file_ => file_.Path).ToArray();

    public IReadOnlyList<string> UnstagedFiles => this.Files.Where(file_ => file_.Unstaged).Select(file_ => file_.Path).ToArray();

    public IReadOnlyList<string> UntrackedFiles => this.Files.Where(file_ => file_.Untracked).Select(file_ => file_.Path).ToArray();
}
