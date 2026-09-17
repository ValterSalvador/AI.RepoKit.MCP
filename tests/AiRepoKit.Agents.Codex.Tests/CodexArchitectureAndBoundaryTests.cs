using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Codex;
using AiRepoKit.Agents.Runtime;
using Xunit;

namespace AiRepoKit.Agents.Codex.Tests;

public sealed class CodexArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedAbstractionsPublicTypeNames =
    [
        "AgentCapability",
        "AgentCapabilitySet",
        "AgentExecutionRequest",
        "AgentExecutionResult",
        "AgentExecutionStatus",
        "AgentProviderId",
        "AgentSessionReference",
        "ExecutionEnvironment",
        "ExecutionPermission",
        "IAgentExecutor",
        "StructuredOutputContract"
    ];

    private static string GetAdapterProjectPath()
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
                "AiRepoKit.Agents.Codex",
                "AiRepoKit.Agents.Codex.csproj"));
    }

    [Fact]
    public void AdapterProject_TargetsNet10()
    {
        string projectPath =
            GetAdapterProjectPath();

        Assert.True(
            File.Exists(projectPath),
            $"Project file does not exist at: {projectPath}");

        XDocument document =
            XDocument.Load(projectPath);

        string? targetFramework =
            document.Descendants("TargetFramework").FirstOrDefault()?.Value;

        Assert.Equal(
            "net10.0",
            targetFramework);
    }

    [Fact]
    public void AdapterProject_HasNullableEnabled()
    {
        string projectPath =
            GetAdapterProjectPath();

        XDocument document =
            XDocument.Load(projectPath);

        string? nullable =
            document.Descendants("Nullable").FirstOrDefault()?.Value;

        Assert.Equal(
            "enable",
            nullable);
    }

    [Fact]
    public void AdapterProject_HasImplicitUsingsEnabled()
    {
        string projectPath =
            GetAdapterProjectPath();

        XDocument document =
            XDocument.Load(projectPath);

        string? implicitUsings =
            document.Descendants("ImplicitUsings").FirstOrDefault()?.Value;

        Assert.Equal(
            "enable",
            implicitUsings);
    }

    [Fact]
    public void AdapterProject_IsPackableFalse()
    {
        string projectPath =
            GetAdapterProjectPath();

        XDocument document =
            XDocument.Load(projectPath);

        string? isPackable =
            document.Descendants("IsPackable").FirstOrDefault()?.Value;

        Assert.Equal(
            "false",
            isPackable);
    }

    [Fact]
    public void AdapterProject_HasZeroPackageReferences()
    {
        string projectPath =
            GetAdapterProjectPath();

        XDocument document =
            XDocument.Load(projectPath);

        IEnumerable<XElement> packageReferences =
            document.Descendants("PackageReference");

        Assert.Empty(packageReferences);
    }

    [Fact]
    public void AdapterProject_HasAuthorizedProjectReferences()
    {
        string projectPath =
            GetAdapterProjectPath();

        XDocument document =
            XDocument.Load(projectPath);

        List<XElement> projectReferences =
            document.Descendants("ProjectReference").ToList();

        Assert.Equal(2, projectReferences.Count);

        List<string> includes = projectReferences
            .Select(r => r.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(includes, inc => inc.Contains("AiRepoKit.Agents.Abstractions.csproj"));
        Assert.Contains(includes, inc => inc.Contains("AiRepoKit.Agents.Runtime.csproj"));
        Assert.DoesNotContain(includes, inc => inc.Contains("AiRepoKit.Spec"));
        Assert.DoesNotContain(includes, inc => inc.Contains("AiRepoKit.Cli"));
        Assert.DoesNotContain(includes, inc => inc.Contains("AiRepoKit.Agents.Antigravity"));
    }

    [Fact]
    public void AdapterAssembly_HasExactlyOnePublicType()
    {
        Assembly assembly =
            typeof(CodexCliClientAdapter).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Single(exportedTypes);
        Assert.Equal(
            "CodexCliClientAdapter",
            exportedTypes[0].Name);
        Assert.Equal(
            "AiRepoKit.Agents.Codex",
            exportedTypes[0].Namespace);
    }

    [Fact]
    public void AdapterClass_ImplementsIAgentExecutor()
    {
        Type adapterType =
            typeof(CodexCliClientAdapter);

        Assert.True(
            typeof(IAgentExecutor).IsAssignableFrom(adapterType),
            "CodexCliClientAdapter must implement IAgentExecutor.");
    }

    [Fact]
    public void AbstractionsAssembly_MaintainsExactlyElevenPublicDomainTypes()
    {
        Assembly assembly =
            typeof(IAgentExecutor).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            11,
            exportedTypes.Length);

        string[] exportedNames =
            exportedTypes
                .Select(type_ => type_.Name)
                .OrderBy(name_ => name_, StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            _expectedAbstractionsPublicTypeNames,
            exportedNames);
    }

    [Fact]
    public void AdapterAssembly_HasNoVendorOrForbiddenReferences()
    {
        Assembly assembly =
            typeof(CodexCliClientAdapter).Assembly;

        AssemblyName[] referencedAssemblies =
            assembly.GetReferencedAssemblies();

        string[] forbiddenPrefixes =
        [
            "Microsoft.Extensions.AI",
            "Microsoft.Agents",
            "ModelContextProtocol",
            "AiRepoKit.Spec",
            "AiRepoKit.Cli",
            "AiRepoKit.Agents.Antigravity",
            "OpenAI",
            "Google",
            "Antigravity"
        ];

        foreach (AssemblyName reference in referencedAssemblies)
        {
            string name =
                reference.Name ?? string.Empty;

            foreach (string forbidden in forbiddenPrefixes)
            {
                Assert.False(
                    name.StartsWith(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"Adapter assembly references forbidden assembly '{name}'.");
            }
        }
    }

    [Fact]
    public void AdapterAssembly_DoesNotExportProcessOrRuntimeContracts()
    {
        Assembly assembly =
            typeof(CodexCliClientAdapter).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        string[] forbiddenWords =
        [
            "Process",
            "Runner",
            "Executor",
            "Runtime",
            "Invocation",
            "Session",
            "LaunchSpec",
            "TempSchema"
        ];

        foreach (Type type in exportedTypes)
        {
            foreach (string word in forbiddenWords)
            {
                Assert.DoesNotContain(
                    word,
                    type.Name,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AdapterAssembly_ContainsNoFuturePhaseConcepts()
    {
        Assembly assembly =
            typeof(CodexCliClientAdapter).Assembly;

        Type[] allTypes =
            assembly.GetTypes();

        string[] forbiddenWords =
        [
            "ModelSelector",
            "Discovery",
            "Route",
            "Router",
            "Retry",
            "Fallback",
            "Workflow",
            "Scheduler",
            "PromptCompiler",
            "TaskDag",
            "Streaming"
        ];

        foreach (Type type in allTypes)
        {
            foreach (string word in forbiddenWords)
            {
                Assert.DoesNotContain(
                    word,
                    type.Name,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Invocation_ContainsNoForbiddenCliFlags()
    {
        AgentExecutionRequest request =
            new(
                "Test instruction",
                ExecutionPermission.WorkspaceWrite,
                new ExecutionEnvironment(AppContext.BaseDirectory));

        ProcessExecutionRequest invocation =
            CodexCliClientAdapter.BuildInvocation(request);

        string[] forbiddenFlags =
        [
            "--model",
            "--profile",
            "--oss",
            "--local-provider",
            "--approve-for-me",
            "--worktree",
            "--add-dir",
            "--image",
            "--last",
            "--all",
            "--continue"
        ];

        foreach (string arg in invocation.Arguments)
        {
            foreach (string forbidden in forbiddenFlags)
            {
                Assert.DoesNotContain(
                    forbidden,
                    arg,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
