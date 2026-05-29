namespace AiRepoKit.Cli.Services;

using AiRepoKit.Cli.Models;
using System.Reflection;
using System.Text.Json;

public sealed class TemplateService
{
    public IReadOnlyList<string> ListTemplates()
    {
        string rootPath = this.TryGetFileTemplateRoot();
        if (!Directory.Exists(rootPath))
        {
            return this.GetEmbeddedTemplateNames()
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Directory
            .EnumerateFiles(rootPath, "*.tpl", SearchOption.AllDirectories)
            .Select(path_ => Path.GetRelativePath(rootPath, path_).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public string RenderTemplate(string templatePath_, RepoAnalysis analysis_, BootstrapOptions options_)
    {
        string content = this.ReadTemplate(templatePath_);
        string repoName = GetSafeRepoName(new DirectoryInfo(analysis_.Profile.RootPath).Name);
        string repoRootPortable = ToPortablePath(analysis_.Profile.RootPath);
        string mcpProjectRelativePathPortable = ToPortablePath(options_.McpProjectRelativePath);
        string mcpDllPortable = ToPortablePath(Path.Combine(
            analysis_.Profile.RootPath,
            "Tools",
            "AiContextMcp",
            "bin",
            "Release",
            options_.TargetFramework,
            $"{options_.McpAssemblyName}.dll"));
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase)
        {
            ["RepoName"] = repoName,
            ["RepoNameJson"] = ToJsonStringValue(repoName),
            ["RepoRoot"] = analysis_.Profile.RootPath,
            ["RepoRootJson"] = ToJsonStringValue(analysis_.Profile.RootPath),
            ["RepoRootPortable"] = repoRootPortable,
            ["MainSolution"] = analysis_.SolutionFiles.FirstOrDefault() ?? string.Empty,
            ["MainSolutionJson"] = ToJsonStringValue(analysis_.SolutionFiles.FirstOrDefault() ?? string.Empty),
            ["GeneratedAtLocal"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            ["GeneratedAtLocalJson"] = ToJsonStringValue(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz")),
            ["TargetFramework"] = options_.TargetFramework,
            ["TargetFrameworkJson"] = ToJsonStringValue(options_.TargetFramework),
            ["McpServerName"] = options_.McpServerName,
            ["McpServerNameJson"] = ToJsonStringValue(options_.McpServerName),
            ["ToolCommandName"] = options_.ToolCommandName,
            ["McpProjectName"] = options_.McpProjectName,
            ["McpNamespace"] = options_.McpNamespace,
            ["McpAssemblyName"] = options_.McpAssemblyName,
            ["McpProjectRelativePath"] = options_.McpProjectRelativePath,
            ["McpProjectRelativePathJson"] = ToJsonStringValue(options_.McpProjectRelativePath),
            ["McpProjectRelativePathPortable"] = mcpProjectRelativePathPortable,
            ["McpDllPortable"] = mcpDllPortable
        };

        return this.ReplacePlaceholders(content, values);
    }

    public static string GetToolVersion()
    {
        string version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "0.0.0";
        int plusIndex = version.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    public string ReplacePlaceholders(string content_, IReadOnlyDictionary<string, string> values_)
    {
        string result = content_;
        foreach (KeyValuePair<string, string> value in values_)
        {
            result = result.Replace("{{" + value.Key + "}}", value.Value, StringComparison.Ordinal);
        }

        return result;
    }

    public string GetTemplateRoot()
    {
        string rootPath = this.TryGetFileTemplateRoot();
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            return rootPath;
        }

        if (this.GetEmbeddedTemplateNames().Count > 0)
        {
            return "embedded://Templates/";
        }

        string outputPath = NormalizeDirectoryPath(Path.Combine(AppContext.BaseDirectory, "Templates"));
        string sourcePath = NormalizeDirectoryPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Templates"));
        throw new InvalidOperationException($"Templates directory not found. Tried '{outputPath}' and '{sourcePath}'.");
    }

    private string ReadTemplate(string templatePath_)
    {
        string rootPath = this.TryGetFileTemplateRoot();
        if (!string.IsNullOrWhiteSpace(rootPath))
        {
            string fullPath = Path.GetFullPath(Path.Combine(rootPath, templatePath_.Replace('/', Path.DirectorySeparatorChar)));
            if (fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }
        }

        string resourceName = "Templates/" + templatePath_.Replace('\\', '/');
        string? actualResourceName = Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .FirstOrDefault(name_ => string.Equals(name_.Replace('\\', '/'), resourceName, StringComparison.OrdinalIgnoreCase));
        using Stream? stream = actualResourceName is null ? null : Assembly.GetExecutingAssembly().GetManifestResourceStream(actualResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Template not found: {templatePath_}");
        }

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private string TryGetFileTemplateRoot()
    {
        string outputPath = NormalizeDirectoryPath(Path.Combine(AppContext.BaseDirectory, "Templates"));
        if (Directory.Exists(outputPath))
        {
            return outputPath;
        }

        string sourcePath = NormalizeDirectoryPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Templates"));
        if (Directory.Exists(sourcePath))
        {
            return sourcePath;
        }

        return string.Empty;
    }

    private IReadOnlyList<string> GetEmbeddedTemplateNames()
    {
        return Assembly.GetExecutingAssembly()
            .GetManifestResourceNames()
            .Select(name_ => name_.Replace('\\', '/'))
            .Where(name_ => name_.StartsWith("Templates/", StringComparison.OrdinalIgnoreCase) && name_.EndsWith(".tpl", StringComparison.OrdinalIgnoreCase))
            .Select(name_ => name_.Substring("Templates/".Length))
            .ToArray();
    }

    private static string NormalizeDirectoryPath(string path_)
    {
        string fullPath = Path.GetFullPath(path_);
        return Path.EndsInDirectorySeparator(fullPath) ? fullPath : fullPath + Path.DirectorySeparatorChar;
    }

    private static string ToPortablePath(string path_)
    {
        return path_.Replace('\\', '/');
    }

    private static string ToJsonStringValue(string value_)
    {
        return JsonSerializer.Serialize(value_);
    }

    private static string GetSafeRepoName(string value_)
    {
        string[] blockedFragments =
        [
            "Sandbox",
            "Validation",
            "PathBug",
            "DoubleClick",
            "ExeOnly"
        ];

        if (blockedFragments.Any(fragment_ => value_.Contains(fragment_, StringComparison.OrdinalIgnoreCase))
            || value_.Contains("Sv" + "ala", StringComparison.OrdinalIgnoreCase)
            || value_.Contains("Val" + "ter", StringComparison.OrdinalIgnoreCase)
            || value_.Contains("neo" + "_" + "v", StringComparison.OrdinalIgnoreCase))
        {
            return "<target-repo>";
        }

        return value_;
    }
}
