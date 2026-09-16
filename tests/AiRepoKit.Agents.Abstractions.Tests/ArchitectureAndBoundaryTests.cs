using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
        "AgentCapability",
        "AgentCapabilitySet",
        "AgentExecutionRequest",
        "AgentExecutionResult",
        "AgentExecutionStatus",
        "AgentProviderId",
        "AgentSessionReference",
        "IAgentExecutor"
    ];

    [Fact]
    public void SourceProject_HasZeroPackageReferences()
    {
        string projectPath =
            Path.GetFullPath(
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

        Assert.True(
            File.Exists(
                projectPath),
            $"Project file does not exist at: {projectPath}");

        XDocument document =
            XDocument.Load(
                projectPath);

        IEnumerable<XElement> packageReferences =
            document.Descendants(
                "PackageReference");

        Assert.Empty(
            packageReferences);
    }

    [Fact]
    public void SourceProject_HasZeroProjectReferences()
    {
        string projectPath =
            Path.GetFullPath(
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

        Assert.True(
            File.Exists(
                projectPath),
            $"Project file does not exist at: {projectPath}");

        XDocument document =
            XDocument.Load(
                projectPath);

        IEnumerable<XElement> projectReferences =
            document.Descendants(
                "ProjectReference");

        Assert.Empty(
            projectReferences);
    }

    [Fact]
    public void Assembly_ReferencesOnlyBclAssemblies()
    {
        Assembly assembly =
            typeof(IAgentExecutor).Assembly;

        AssemblyName[] referencedAssemblies =
            assembly.GetReferencedAssemblies();

        foreach (AssemblyName reference in referencedAssemblies)
        {
            string name =
                reference.Name ??
                string.Empty;

            Assert.True(
                name == "System.Runtime" ||
                name.StartsWith(
                    "System.",
                    StringComparison.Ordinal),
                $"Unexpected referenced assembly: {name}");
        }
    }

    [Fact]
    public void PublicDomainSurface_IsLimitedToEightFrozenTypes()
    {
        Assembly assembly =
            typeof(IAgentExecutor).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        Assert.Equal(
            8,
            exportedTypes.Length);

        string[] exportedNames =
            exportedTypes
                .Select(
                    type_ =>
                        type_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            _expectedPublicTypeNames,
            exportedNames);
    }

    [Fact]
    public void PublicApi_ContainsNoVendorOrFrameworkTypes()
    {
        Assembly assembly =
            typeof(IAgentExecutor).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        string[] forbiddenNamespaces =
        [
            "Microsoft.Extensions.AI",
            "Microsoft.Agents",
            "ModelContextProtocol",
            "AiRepoKit.Spec",
            "AiRepoKit.Cli"
        ];

        foreach (Type type in exportedTypes)
        {
            foreach (string forbidden in forbiddenNamespaces)
            {
                Assert.DoesNotContain(
                    forbidden,
                    type.Namespace ??
                    string.Empty);
            }

            foreach (MethodInfo method in type.GetMethods(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                AssertForbiddenType(
                    method.ReturnType,
                    forbiddenNamespaces);

                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertForbiddenType(
                        parameter.ParameterType,
                        forbiddenNamespaces);
                }
            }

            foreach (PropertyInfo property in type.GetProperties(
                         BindingFlags.Public |
                         BindingFlags.Instance |
                         BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                AssertForbiddenType(
                    property.PropertyType,
                    forbiddenNamespaces);
            }
        }
    }

    private static void AssertForbiddenType(
        Type type_,
        string[] forbiddenNamespaces_)
    {
        string? ns =
            type_.Namespace;

        if (ns is null)
        {
            return;
        }

        foreach (string forbidden in forbiddenNamespaces_)
        {
            Assert.False(
                ns.StartsWith(
                    forbidden,
                    StringComparison.Ordinal),
                $"Public API exposed forbidden type '{type_.FullName}' from namespace '{ns}'.");
        }
    }

    [Fact]
    public void AgentExecutionRequest_ContainsNoCapabilityRequirement()
    {
        Type requestType =
            typeof(AgentExecutionRequest);

        PropertyInfo? capabilityProperty =
            requestType.GetProperty("Capability");
        PropertyInfo? capabilitiesProperty =
            requestType.GetProperty("Capabilities");
        PropertyInfo? requiredCapabilitiesProperty =
            requestType.GetProperty("RequiredCapabilities");

        Assert.Null(capabilityProperty);
        Assert.Null(capabilitiesProperty);
        Assert.Null(requiredCapabilitiesProperty);

        foreach (ConstructorInfo ctor in requestType.GetConstructors())
        {
            foreach (ParameterInfo parameter in ctor.GetParameters())
            {
                Assert.DoesNotContain(
                    "capability",
                    parameter.Name ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void AgentExecutionResult_ContainsNoCapabilityData()
    {
        Type resultType =
            typeof(AgentExecutionResult);

        PropertyInfo? capabilityProperty =
            resultType.GetProperty("Capability");
        PropertyInfo? capabilitiesProperty =
            resultType.GetProperty("Capabilities");
        PropertyInfo? discoveredCapabilitiesProperty =
            resultType.GetProperty("DiscoveredCapabilities");

        Assert.Null(capabilityProperty);
        Assert.Null(capabilitiesProperty);
        Assert.Null(discoveredCapabilitiesProperty);

        foreach (MethodInfo method in resultType.GetMethods())
        {
            Assert.DoesNotContain(
                "Capability",
                method.Name,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Assembly_DoesNotContainFuturePhaseConcepts()
    {
        Assembly assembly =
            typeof(IAgentExecutor).Assembly;

        string[] forbiddenTypeNames =
        [
            "ExecutionPermission",
            "ExecutionEnvironment",
            "StructuredOutputContract",
            "ExecutableWork",
            "PromptCompiler",
            "TaskDag",
            "WorkflowScheduler"
        ];

        Type[] exportedTypes =
            assembly.GetExportedTypes();

        foreach (string forbidden in forbiddenTypeNames)
        {
            Assert.DoesNotContain(
                exportedTypes,
                type_ =>
                    string.Equals(
                        type_.Name,
                        forbidden,
                        StringComparison.OrdinalIgnoreCase));
        }
    }
}
