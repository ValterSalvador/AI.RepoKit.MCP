using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Antigravity;
using Xunit;

namespace AiRepoKit.Agents.Antigravity.Tests;

public sealed class AntigravityArchitectureAndBoundaryTests
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
                "AiRepoKit.Agents.Antigravity",
                "AiRepoKit.Agents.Antigravity.csproj"));
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
    public void AdapterProject_HasExactlyOneProjectReference_ToAbstractions()
    {
        string projectPath =
            GetAdapterProjectPath();

        XDocument document =
            XDocument.Load(projectPath);

        List<XElement> projectReferences =
            document.Descendants("ProjectReference").ToList();

        Assert.Single(projectReferences);

        string include =
            projectReferences[0].Attribute("Include")?.Value ?? string.Empty;

        Assert.Contains(
            "AiRepoKit.Agents.Abstractions.csproj",
            include);
        Assert.DoesNotContain(
            "AiRepoKit.Spec",
            include);
        Assert.DoesNotContain(
            "AiRepoKit.Cli",
            include);
    }

    [Fact]
    public void AdapterAssembly_HasExactlyOnePublicType()
    {
        Assembly assembly =
            typeof(AntigravityCliClientAdapter).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Single(exportedTypes);
        Assert.Equal(
            "AntigravityCliClientAdapter",
            exportedTypes[0].Name);
        Assert.Equal(
            "AiRepoKit.Agents.Antigravity",
            exportedTypes[0].Namespace);
    }

    [Fact]
    public void AdapterClass_ImplementsIAgentExecutor()
    {
        Type adapterType =
            typeof(AntigravityCliClientAdapter);

        Assert.True(
            typeof(IAgentExecutor).IsAssignableFrom(adapterType),
            "AntigravityCliClientAdapter must implement IAgentExecutor.");
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
            typeof(AntigravityCliClientAdapter).Assembly;

        AssemblyName[] referencedAssemblies =
            assembly.GetReferencedAssemblies();

        string[] forbiddenPrefixes =
        [
            "Microsoft.Extensions.AI",
            "Microsoft.Agents",
            "ModelContextProtocol",
            "AiRepoKit.Spec",
            "AiRepoKit.Cli",
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
            typeof(AntigravityCliClientAdapter).Assembly;

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
            "LaunchSpec"
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
            typeof(AntigravityCliClientAdapter).Assembly;

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

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        string[] forbiddenFlags =
        [
            "--model",
            "--effort",
            "--agent",
            "--continue",
            "--print-timeout",
            "stream-json",
            "--mode=plan"
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
