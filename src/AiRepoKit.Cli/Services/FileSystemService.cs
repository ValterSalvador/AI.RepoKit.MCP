using System.Diagnostics;
using AiRepoKit.Cli.Models;

namespace AiRepoKit.Cli.Services;

public sealed class FileSystemService
{
    public void EnsureDirectory(string rootPath_, string relativePath_, bool dryRun_)
    {
        string fullPath = this.NormalizePath(rootPath_, relativePath_);
        if (!this.IsInsideRoot(rootPath_, fullPath))
        {
            throw new InvalidOperationException($"Path is outside repository root: {relativePath_}");
        }

        if (!dryRun_)
        {
            Directory.CreateDirectory(fullPath);
        }
    }

    public void WriteFile(string rootPath_, string relativePath_, string content_, BootstrapOptions options_, bool allowExistingOverwrite_ = false)
    {
        string fullPath = this.NormalizePath(rootPath_, relativePath_);
        if (!this.IsInsideRoot(rootPath_, fullPath))
        {
            throw new InvalidOperationException($"Path is outside repository root: {relativePath_}");
        }

        if (this.IsRestrictedPath(relativePath_))
        {
            throw new InvalidOperationException($"Path is restricted: {relativePath_}");
        }

        bool exists = File.Exists(fullPath);
        if (exists && !allowExistingOverwrite_ && !options_.Backup && !options_.Force)
        {
            throw new InvalidOperationException($"File exists and requires --backup or --force: {relativePath_}");
        }

        if (options_.DryRun)
        {
            return;
        }

        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (exists && options_.Backup)
        {
            this.CreateBackup(rootPath_, relativePath_);
        }

        File.WriteAllText(fullPath, content_);
    }

    public bool FileExists(string rootPath_, string relativePath_)
    {
        return File.Exists(this.NormalizePath(rootPath_, relativePath_));
    }

    public bool DirectoryExists(string rootPath_, string relativePath_)
    {
        return Directory.Exists(this.NormalizePath(rootPath_, relativePath_));
    }

    public string CreateBackup(string rootPath_, string relativePath_)
    {
        string fullPath = this.NormalizePath(rootPath_, relativePath_);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File to backup was not found.", fullPath);
        }

        string backupPath = $"{fullPath}.{DateTimeOffset.Now:yyyyMMddHHmmss}.bak";
        File.Copy(fullPath, backupPath, false);
        return backupPath;
    }

    public string NormalizePath(string rootPath_, string relativePath_)
    {
        string rootPath = Path.GetFullPath(rootPath_);
        return Path.GetFullPath(Path.Combine(rootPath, relativePath_));
    }

    public bool IsInsideRoot(string rootPath_, string path_)
    {
        string rootPath = Path.GetFullPath(rootPath_).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path_);
        return fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsRestrictedPath(string relativePath_)
    {
        string normalized = relativePath_.Replace('\\', '/').TrimStart('/');
        string fileName = Path.GetFileName(normalized);
        string lower = normalized.ToLowerInvariant();
        string lowerFileName = fileName.ToLowerInvariant();
        bool isVisualStudioMcpConfig = string.Equals(lower, ".vs/mcp.json", StringComparison.Ordinal);

        if (lower.StartsWith(".git/", StringComparison.Ordinal)
            || lower.StartsWith("bin/", StringComparison.Ordinal)
            || lower.StartsWith("obj/", StringComparison.Ordinal)
            || (lower.StartsWith(".vs/", StringComparison.Ordinal) && !isVisualStudioMcpConfig)
            || lower.StartsWith("oracle-data/", StringComparison.Ordinal))
        {
            return true;
        }

        if (lower.Contains("/bin/", StringComparison.Ordinal)
            || lower.Contains("/obj/", StringComparison.Ordinal)
            || lower.Contains("/.git/", StringComparison.Ordinal)
            || (lower.Contains("/.vs/", StringComparison.Ordinal) && !isVisualStudioMcpConfig)
            || lower.Contains("/migrations/", StringComparison.Ordinal)
            || lower.Contains("/wwwroot/uploads/", StringComparison.Ordinal))
        {
            return true;
        }

        return lowerFileName.StartsWith("appsettings", StringComparison.Ordinal) && lowerFileName.EndsWith(".json", StringComparison.Ordinal)
            || lowerFileName.StartsWith("docker-compose", StringComparison.Ordinal) && (lowerFileName.EndsWith(".yml", StringComparison.Ordinal) || lowerFileName.EndsWith(".yaml", StringComparison.Ordinal))
            || lowerFileName == "key.json"
            || lowerFileName.EndsWith(".pfx", StringComparison.Ordinal)
            || lowerFileName.EndsWith(".pem", StringComparison.Ordinal)
            || lowerFileName.EndsWith(".key", StringComparison.Ordinal)
            || lowerFileName.EndsWith(".jks", StringComparison.Ordinal)
            || lowerFileName.EndsWith(".keystore", StringComparison.Ordinal);
    }

    public string GetDotNetSdkVersion()
    {
        return RunAndRead("dotnet", "--version");
    }

    public bool HasDotNet10Sdk()
    {
        string sdks = RunAndRead("dotnet", "--list-sdks");
        return sdks.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Any(line_ => line_.StartsWith("10.", StringComparison.OrdinalIgnoreCase));
    }

    public string GetOperatingSystem()
    {
        return OperatingSystem.IsWindows()
            ? $"Windows {Environment.OSVersion.Version}"
            : Environment.OSVersion.ToString();
    }

    private static string RunAndRead(string fileName_, string argument_)
    {
        try
        {
            using Process process = new();
            process.StartInfo.FileName = fileName_;
            process.StartInfo.ArgumentList.Add(argument_);
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : "Unavailable";
        }
        catch
        {
            return "Unavailable";
        }
    }
}
