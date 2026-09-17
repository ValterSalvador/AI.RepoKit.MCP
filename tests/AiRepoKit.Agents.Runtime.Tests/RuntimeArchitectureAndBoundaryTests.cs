namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class RuntimeArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicRuntimeTypes =
    [
        "ChatClientModelExecutionRuntime",
        "IModelExecutionRuntime",
        "IProcessExecutionRuntime",
        "ModelExecutionRequest",
        "ModelExecutionResult",
        "ProcessExecutionRequest",
        "ProcessExecutionResult",
        "SystemProcessExecutionRuntime"
    ];

    private static string GetRuntimeProjectPath()
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "AiRepoKit.Agents.Runtime",
                "AiRepoKit.Agents.Runtime.csproj"));
    }

    [Fact]
    public void RuntimeProject_TargetsNet10()
    {
        string projectPath = GetRuntimeProjectPath();
        Assert.True(File.Exists(projectPath), $"Project file does not exist: {projectPath}");

        XDocument document = XDocument.Load(projectPath);
        string? targetFramework = document.Descendants("TargetFramework").FirstOrDefault()?.Value;
        Assert.Equal("net10.0", targetFramework);
    }

    [Fact]
    public void RuntimeProject_HasNullableEnabled()
    {
        string projectPath = GetRuntimeProjectPath();
        XDocument document = XDocument.Load(projectPath);
        string? nullable = document.Descendants("Nullable").FirstOrDefault()?.Value;
        Assert.Equal("enable", nullable);
    }

    [Fact]
    public void RuntimeProject_HasImplicitUsingsEnabled()
    {
        string projectPath = GetRuntimeProjectPath();
        XDocument document = XDocument.Load(projectPath);
        string? implicitUsings = document.Descendants("ImplicitUsings").FirstOrDefault()?.Value;
        Assert.Equal("enable", implicitUsings);
    }

    [Fact]
    public void RuntimeProject_IsPackableFalse()
    {
        string projectPath = GetRuntimeProjectPath();
        XDocument document = XDocument.Load(projectPath);
        string? isPackable = document.Descendants("IsPackable").FirstOrDefault()?.Value;
        Assert.Equal("false", isPackable);
    }

    [Fact]
    public void RuntimeProject_HasOnlyAuthorizedPackageReference_MicrosoftExtensionsAIAbstractions()
    {
        string projectPath = GetRuntimeProjectPath();
        XDocument document = XDocument.Load(projectPath);

        List<XElement> packageReferences = document.Descendants("PackageReference").ToList();
        Assert.Single(packageReferences);

        XElement packageRef = packageReferences[0];
        Assert.Equal("Microsoft.Extensions.AI.Abstractions", packageRef.Attribute("Include")?.Value);
        Assert.Equal("10.10.0", packageRef.Attribute("Version")?.Value);
    }

    [Fact]
    public void RuntimeProject_HasExactlyOneProjectReference_ToAbstractions()
    {
        string projectPath = GetRuntimeProjectPath();
        XDocument document = XDocument.Load(projectPath);

        List<XElement> projectReferences = document.Descendants("ProjectReference").ToList();
        Assert.Single(projectReferences);

        string include = projectReferences[0].Attribute("Include")?.Value ?? string.Empty;
        Assert.Contains("AiRepoKit.Agents.Abstractions.csproj", include);
        Assert.DoesNotContain("AiRepoKit.Spec", include);
        Assert.DoesNotContain("AiRepoKit.Cli", include);
        Assert.DoesNotContain("AiRepoKit.Agents.Antigravity", include);
        Assert.DoesNotContain("AiRepoKit.Agents.Codex", include);
    }

    [Fact]
    public void RuntimeAssembly_ExportsExactlyEightAuthorizedPublicTypes()
    {
        Assembly assembly = typeof(IProcessExecutionRuntime).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();

        string[] exportedNames = exportedTypes
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(8, exportedTypes.Length);
        Assert.Equal(_expectedPublicRuntimeTypes, exportedNames);

        foreach (Type type in exportedTypes)
        {
            Assert.Equal("AiRepoKit.Agents.Runtime", type.Namespace);
        }
    }

    [Fact]
    public void RuntimeAssembly_DoesNotReferenceSpecOrCliOrAdapters()
    {
        Assembly assembly = typeof(IProcessExecutionRuntime).Assembly;
        AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();

        string[] forbiddenPrefixes =
        [
            "AiRepoKit.Spec",
            "AiRepoKit.Cli",
            "AiRepoKit.Agents.Antigravity",
            "AiRepoKit.Agents.Codex",
            "Microsoft.Agents",
            "ModelContextProtocol",
            "OpenAI",
            "Google"
        ];

        foreach (AssemblyName refName in referencedAssemblies)
        {
            string name = refName.Name ?? string.Empty;
            foreach (string forbidden in forbiddenPrefixes)
            {
                Assert.False(
                    name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Runtime assembly references forbidden assembly '{name}'.");
            }
        }
    }

    [Fact]
    public void AbstractionsAssembly_DoesNotReferenceMicrosoftExtensionsAI()
    {
        Assembly assembly = typeof(IAgentExecutor).Assembly;
        AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();

        foreach (AssemblyName refName in referencedAssemblies)
        {
            string name = refName.Name ?? string.Empty;
            Assert.False(
                name.StartsWith("Microsoft.Extensions.AI", StringComparison.OrdinalIgnoreCase),
                $"Abstractions assembly must not reference Microsoft.Extensions.AI, but found '{name}'.");
        }
    }

    [Fact]
    public void AbstractionsProject_HasZeroPackageReferences()
    {
        string projectPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "AiRepoKit.Agents.Abstractions",
                "AiRepoKit.Agents.Abstractions.csproj"));

        XDocument document = XDocument.Load(projectPath);
        IEnumerable<XElement> packageReferences = document.Descendants("PackageReference");
        Assert.Empty(packageReferences);
    }
}
