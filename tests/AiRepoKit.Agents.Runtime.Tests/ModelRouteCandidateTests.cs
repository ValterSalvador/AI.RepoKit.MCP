namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelRouteCandidateTests
{
    private static ModelRuntimeRegistration CreateRegistration(
        string providerId_ = "provider-a",
        string modelId_ = "model-1")
    {
        return new ModelRuntimeRegistration(
            new AgentProviderId(providerId_),
            modelId_,
            AgentCapabilitySet.Empty,
            new TestModelExecutionRuntime());
    }

    [Fact]
    public void Constructor_NullRegistration_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelRouteCandidate(
                null!,
                ModelHealthStatus.Healthy));
    }

    [Fact]
    public void Constructor_ValidHealthy_RetainsExactRegistrationAndStatus()
    {
        ModelRuntimeRegistration reg =
            CreateRegistration();

        ModelRouteCandidate candidate =
            new(reg, ModelHealthStatus.Healthy);

        Assert.Same(reg, candidate.Registration);
        Assert.Equal(ModelHealthStatus.Healthy, candidate.HealthStatus);
    }

    [Fact]
    public void Constructor_ValidUnknown_Accepted()
    {
        ModelRuntimeRegistration reg =
            CreateRegistration();

        ModelRouteCandidate candidate =
            new(reg, ModelHealthStatus.Unknown);

        Assert.Same(reg, candidate.Registration);
        Assert.Equal(ModelHealthStatus.Unknown, candidate.HealthStatus);
    }

    [Fact]
    public void Constructor_ValidUnhealthy_AcceptedByValueObject()
    {
        ModelRuntimeRegistration reg =
            CreateRegistration();

        ModelRouteCandidate candidate =
            new(reg, ModelHealthStatus.Unhealthy);

        Assert.Same(reg, candidate.Registration);
        Assert.Equal(ModelHealthStatus.Unhealthy, candidate.HealthStatus);
    }

    [Fact]
    public void Constructor_UndefinedHealthStatus_ThrowsArgumentOutOfRangeException()
    {
        ModelRuntimeRegistration reg =
            CreateRegistration();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelRouteCandidate(
                reg,
                (ModelHealthStatus)999));
    }

    [Fact]
    public void PublicProperties_ExactlyRegistrationAndHealthStatus()
    {
        PropertyInfo[] properties =
            typeof(ModelRouteCandidate).GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        string[] propertyNames =
            properties
                .Select(p => p.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

        string[] expected =
        [
            "HealthStatus",
            "Registration"
        ];

        Assert.Equal(expected, propertyNames);
    }

    [Fact]
    public void PublicSurface_DoesNotExposeScoreOrRankOrTelemetry()
    {
        MemberInfo[] members =
            typeof(ModelRouteCandidate).GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        string[] forbiddenNames =
        [
            "Score",
            "Rank",
            "Priority",
            "Weight",
            "BenchmarkResult",
            "Latency",
            "Cost",
            "TokenEstimate"
        ];

        foreach (MemberInfo member in members)
        {
            foreach (string forbidden in forbiddenNames)
            {
                Assert.DoesNotContain(
                    forbidden,
                    member.Name,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
