namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelRuntimeRegistrationTests
{
    private static readonly AgentProviderId _defaultProviderId = new("test-provider");
    private static readonly AgentCapabilitySet _defaultCapabilities = AgentCapabilitySet.Empty;
    private static readonly IModelExecutionRuntime _defaultRuntime = new TestModelExecutionRuntime();

    [Fact]
    public void Constructor_NullProviderId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelRuntimeRegistration(
                null!,
                "model-1",
                _defaultCapabilities,
                _defaultRuntime));
    }

    [Fact]
    public void Constructor_NullModelId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelRuntimeRegistration(
                _defaultProviderId,
                null!,
                _defaultCapabilities,
                _defaultRuntime));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n")]
    public void Constructor_BlankModelId_ThrowsArgumentException(string blankModelId)
    {
        Assert.Throws<ArgumentException>(
            () => new ModelRuntimeRegistration(
                _defaultProviderId,
                blankModelId,
                _defaultCapabilities,
                _defaultRuntime));
    }

    [Fact]
    public void Constructor_PreservesExactNormalModelId()
    {
        string modelId = "gpt-4-turbo";
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            modelId,
            _defaultCapabilities,
            _defaultRuntime);

        Assert.Same(modelId, registration.ModelId);
    }

    [Fact]
    public void Constructor_PreservesLeadingAndTrailingSpacesOnNonWhitespaceModelId()
    {
        string modelId = "  gpt-4-turbo  ";
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            modelId,
            _defaultCapabilities,
            _defaultRuntime);

        Assert.Equal("  gpt-4-turbo  ", registration.ModelId);
        Assert.Same(modelId, registration.ModelId);
    }

    [Fact]
    public void Constructor_PreservesCaseExactly()
    {
        string modelId = "Gpt-4-Turbo-Preview";
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            modelId,
            _defaultCapabilities,
            _defaultRuntime);

        Assert.Equal("Gpt-4-Turbo-Preview", registration.ModelId);
        Assert.Same(modelId, registration.ModelId);
    }

    [Fact]
    public void Constructor_NullCapabilities_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelRuntimeRegistration(
                _defaultProviderId,
                "model-1",
                null!,
                _defaultRuntime));
    }

    [Fact]
    public void Constructor_EmptyCapabilities_Accepted()
    {
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            "model-1",
            AgentCapabilitySet.Empty,
            _defaultRuntime);

        Assert.Empty(registration.Capabilities);
        Assert.Same(AgentCapabilitySet.Empty, registration.Capabilities);
    }

    [Fact]
    public void Constructor_RetainsExactCapabilitySetReference()
    {
        AgentCapabilitySet capabilities = new([new AgentCapability("text")]);
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            "model-1",
            capabilities,
            _defaultRuntime);

        Assert.Same(capabilities, registration.Capabilities);
    }

    [Fact]
    public void Constructor_NullRuntime_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelRuntimeRegistration(
                _defaultProviderId,
                "model-1",
                _defaultCapabilities,
                null!));
    }

    [Fact]
    public void Constructor_RetainsExactRuntimeReference()
    {
        TestModelExecutionRuntime runtime = new();
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            "model-1",
            _defaultCapabilities,
            runtime);

        Assert.Same(runtime, registration.Runtime);
    }

    [Fact]
    public void Constructor_NullHealthProbe_Accepted()
    {
        ModelRuntimeRegistration registration = new(
            _defaultProviderId,
            "model-1",
            _defaultCapabilities,
            _defaultRuntime,
            null);

        Assert.NotNull(registration);
    }

    [Fact]
    public void HealthProbe_IsNotPublicPropertyFieldEventOrMethod()
    {
        Type registrationType = typeof(ModelRuntimeRegistration);

        MemberInfo[] publicMembers = registrationType.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        foreach (MemberInfo member in publicMembers)
        {
            Assert.DoesNotContain(
                "probe",
                member.Name,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "health",
                member.Name,
                StringComparison.OrdinalIgnoreCase);
        }

        PropertyInfo[] publicProperties = registrationType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        string[] propertyNames = publicProperties
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] expectedPropertyNames =
        [
            "Capabilities",
            "ModelId",
            "ProviderId",
            "Runtime"
        ];

        Assert.Equal(expectedPropertyNames, propertyNames);
    }
}
