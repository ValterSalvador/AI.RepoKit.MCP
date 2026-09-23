namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using System.Xml.Linq;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Microsoft.Extensions.AI;
using Xunit;

public sealed class RuntimeArchitectureAndBoundaryTests
{
    private static readonly string[] _expectedPublicRuntimeTypes =
    [
        "ChatClientModelExecutionRuntime",
        "ConfiguredModelDiscoveryRuntime",
        "DeterministicModelRoutingRuntime",
        "IModelDiscoveryRuntime",
        "IModelExecutionFailureClassifier",
        "IModelExecutionRuntime",
        "IModelRoutingRuntime",
        "IModelSessionRuntime",
        "IModelStreamingExecutionRuntime",
        "IProcessExecutionRuntime",
        "IResumableModelSessionRuntime",
        "ModelExecutionRequest",
        "ModelExecutionResult",
        "ModelExecutionTelemetry",
        "ModelExecutionUpdate",
        "ModelHealthSnapshot",
        "ModelHealthStatus",
        "ModelRouteCandidate",
        "ModelRuntimeRegistration",
        "ModelRuntimeResiliencePolicy",
        "ModelTokenPricing",
        "ModelTokenUsage",
        "ProcessExecutionRequest",
        "ProcessExecutionResult",
        "ResilientModelExecutionRuntime",
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
    public void RuntimeAssembly_ExportsExactlyTwentySixAuthorizedPublicTypes()
    {
        Assembly assembly = typeof(IProcessExecutionRuntime).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();

        string[] exportedNames = exportedTypes
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(26, exportedTypes.Length);
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

    [Fact]
    public void RuntimePublicSurface_DoesNotExposeMicrosoftExtensionsAITypes()
    {
        Type[] p04P05P06AndP07Types =
        [
            typeof(ConfiguredModelDiscoveryRuntime),
            typeof(DeterministicModelRoutingRuntime),
            typeof(IModelDiscoveryRuntime),
            typeof(IModelRoutingRuntime),
            typeof(ModelHealthSnapshot),
            typeof(ModelHealthStatus),
            typeof(ModelRouteCandidate),
            typeof(ModelRuntimeRegistration),
            typeof(ModelExecutionTelemetry),
            typeof(ModelTokenPricing),
            typeof(ModelTokenUsage),
            typeof(ModelExecutionResult),
            typeof(ModelExecutionUpdate),
            typeof(IModelExecutionFailureClassifier),
            typeof(IResumableModelSessionRuntime),
            typeof(ModelRuntimeResiliencePolicy),
            typeof(ResilientModelExecutionRuntime)
        ];

        foreach (Type type in p04P05P06AndP07Types)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AssertNotMicrosoftExtensionsAi(method.ReturnType);
                foreach (ParameterInfo param in method.GetParameters())
                {
                    AssertNotMicrosoftExtensionsAi(param.ParameterType);
                }
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AssertNotMicrosoftExtensionsAi(property.PropertyType);
            }

            foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (ParameterInfo param in ctor.GetParameters())
                {
                    AssertNotMicrosoftExtensionsAi(param.ParameterType);
                }
            }
        }
    }

    [Fact]
    public void ModelExecutionResult_TelemetryProperty_ReturnsModelExecutionTelemetryType()
    {
        PropertyInfo? prop = typeof(ModelExecutionResult).GetProperty("Telemetry", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(ModelExecutionTelemetry), prop.PropertyType);
    }

    [Fact]
    public void ModelExecutionUpdate_TelemetryProperty_ReturnsModelExecutionTelemetryType()
    {
        PropertyInfo? prop = typeof(ModelExecutionUpdate).GetProperty("Telemetry", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(ModelExecutionTelemetry), prop.PropertyType);
    }

    [Fact]
    public void ChatClientModelExecutionRuntime_PublicConstructors_RetainExactCount()
    {
        ConstructorInfo[] publicConstructors = typeof(ChatClientModelExecutionRuntime)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Equal(2, publicConstructors.Length);

        List<Type[]> parameterSignatures = publicConstructors
            .Select(c => c.GetParameters().Select(p => p.ParameterType).ToArray())
            .ToList();

        Assert.Contains(
            parameterSignatures,
            sig => sig.SequenceEqual([typeof(IChatClient)]));

        Assert.Contains(
            parameterSignatures,
            sig => sig.SequenceEqual([typeof(IChatClient), typeof(ModelTokenPricing)]));

        Assert.DoesNotContain(
            publicConstructors,
            c => c.GetParameters().Any(p => p.ParameterType == typeof(TimeProvider)));

        Assert.DoesNotContain(
            parameterSignatures,
            sig => sig.SequenceEqual([typeof(IChatClient), typeof(TimeProvider)]));
    }

    [Fact]
    public void RuntimeInterfaces_RetainExactSignaturesWithoutMutation()
    {
        Type[] runtimeInterfaces =
        [
            typeof(IModelExecutionRuntime),
            typeof(IModelSessionRuntime),
            typeof(IModelStreamingExecutionRuntime),
            typeof(IModelDiscoveryRuntime),
            typeof(IModelRoutingRuntime),
            typeof(IProcessExecutionRuntime)
        ];

        foreach (Type iface in runtimeInterfaces)
        {
            Assert.True(iface.IsInterface);
            foreach (MethodInfo method in iface.GetMethods())
            {
                AssertNotMicrosoftExtensionsAi(method.ReturnType);
                foreach (ParameterInfo param in method.GetParameters())
                {
                    AssertNotMicrosoftExtensionsAi(param.ParameterType);
                }
            }
        }
    }

    [Fact]
    public void RuntimeAssembly_DoesNotExportModelRequirement()
    {
        Assembly assembly = typeof(IProcessExecutionRuntime).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();

        Assert.DoesNotContain(
            exportedTypes,
            t => t.Name.Contains("ModelRequirement", StringComparison.OrdinalIgnoreCase)
              || t.Name.Contains("AgentRequirement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AbstractionsAssembly_DoesNotExportModelRequirement()
    {
        Assembly assembly = typeof(IAgentExecutor).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();

        Assert.DoesNotContain(
            exportedTypes,
            t => t.Name.Contains("ModelRequirement", StringComparison.OrdinalIgnoreCase)
              || t.Name.Contains("AgentRequirement", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AbstractionsPublicSurface_RemainsUntouched()
    {
        Assembly assembly = typeof(IAgentExecutor).Assembly;
        Type[] exportedTypes = assembly.GetExportedTypes();

        Assert.Equal(11, exportedTypes.Length);
    }

    [Fact]
    public void IModelExecutionFailureClassifier_DeclaresExactlyOneMethod_WithAuthorizedSignature()
    {
        Type classifierType = typeof(IModelExecutionFailureClassifier);
        Assert.True(classifierType.IsInterface);

        MethodInfo[] methods = classifierType.GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Single(methods);

        MethodInfo method = methods[0];
        Assert.Equal("IsRetryable", method.Name);
        Assert.Equal(typeof(bool), method.ReturnType);

        ParameterInfo[] parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ModelRouteCandidate), parameters[0].ParameterType);
        Assert.Equal(typeof(Exception), parameters[1].ParameterType);
    }

    [Fact]
    public void IResumableModelSessionRuntime_ExtendsStreamingRuntime_WithZeroDeclaredMembers()
    {
        Type resumableType = typeof(IResumableModelSessionRuntime);
        Assert.True(resumableType.IsInterface);
        Assert.Contains(typeof(IModelStreamingExecutionRuntime), resumableType.GetInterfaces());

        MemberInfo[] declaredMembers = resumableType.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Empty(declaredMembers);
    }

    [Fact]
    public void ChatClientModelExecutionRuntime_ImplementsIResumableModelSessionRuntime()
    {
        Type chatClientRuntimeType = typeof(ChatClientModelExecutionRuntime);
        Assert.Contains(typeof(IResumableModelSessionRuntime), chatClientRuntimeType.GetInterfaces());
    }

    [Fact]
    public void ModelRuntimeResiliencePolicy_DeclaresExactlyTwoReadOnlyProperties_AndOneConstructor()
    {
        Type policyType = typeof(ModelRuntimeResiliencePolicy);
        Assert.True(policyType.IsSealed);

        PropertyInfo[] properties = policyType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(2, properties.Length);

        PropertyInfo? maxAttemptsProp = policyType.GetProperty("MaxAttemptsPerCandidate", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(maxAttemptsProp);
        Assert.Equal(typeof(int), maxAttemptsProp.PropertyType);
        Assert.True(maxAttemptsProp.CanRead);
        Assert.Null(maxAttemptsProp.GetSetMethod());

        PropertyInfo? backoffProp = policyType.GetProperty("RetryBackoff", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(backoffProp);
        Assert.Equal(typeof(TimeSpan), backoffProp.PropertyType);
        Assert.True(backoffProp.CanRead);
        Assert.Null(backoffProp.GetSetMethod());

        ConstructorInfo[] constructors = policyType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(constructors);

        ParameterInfo[] parameters = constructors[0].GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal(typeof(TimeSpan), parameters[1].ParameterType);
    }

    [Fact]
    public void ResilientModelExecutionRuntime_ImplementsResumableInterface_AndDeclaresExactlyOnePublicConstructor()
    {
        Type runtimeType = typeof(ResilientModelExecutionRuntime);
        Assert.True(runtimeType.IsSealed);
        Assert.Contains(typeof(IResumableModelSessionRuntime), runtimeType.GetInterfaces());

        ConstructorInfo[] publicConstructors = runtimeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(publicConstructors);

        ParameterInfo[] parameters = publicConstructors[0].GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal(typeof(IModelRoutingRuntime), parameters[0].ParameterType);
        Assert.Equal(typeof(AgentCapabilitySet), parameters[1].ParameterType);
        Assert.Equal(typeof(ModelRuntimeResiliencePolicy), parameters[2].ParameterType);
        Assert.Equal(typeof(IModelExecutionFailureClassifier), parameters[3].ParameterType);

        Assert.DoesNotContain(
            publicConstructors,
            c => c.GetParameters().Any(p => p.ParameterType == typeof(TimeProvider)));
    }

    private static void AssertNotMicrosoftExtensionsAi(Type type_)
    {
        Type toCheck = type_;
        if (toCheck.IsGenericType)
        {
            foreach (Type genericArg in toCheck.GetGenericArguments())
            {
                AssertNotMicrosoftExtensionsAi(genericArg);
            }
        }

        string? ns = toCheck.Namespace;
        if (ns is not null)
        {
            Assert.False(
                ns.StartsWith("Microsoft.Extensions.AI", StringComparison.Ordinal),
                $"Public contract exposed Microsoft.Extensions.AI type: {toCheck.FullName}");
        }
    }
}
