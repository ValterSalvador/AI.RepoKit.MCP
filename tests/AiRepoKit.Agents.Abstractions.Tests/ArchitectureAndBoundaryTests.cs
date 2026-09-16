using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class ArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicTypeNames =
    [
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
    public void PublicP01DomainSurface_IsLimitedToSixFrozenTypes()
    {
        Assembly assembly =
            typeof(IAgentExecutor).Assembly;

        Type[] exportedTypes =
            assembly.GetExportedTypes();

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
}
