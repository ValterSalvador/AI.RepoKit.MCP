namespace AiRepoKit.Cli.Models;

public sealed record RepoProfile
{
    public RepoProfile(string rootPath_, bool exists_)
    {
        this.RootPath = rootPath_;
        this.Exists = exists_;
    }

    public string RootPath { get; }

    public bool Exists { get; }
}
