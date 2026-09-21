namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelHealthSnapshotTests
{
    private static readonly AgentProviderId _defaultProviderId = new("test-provider");

    [Fact]
    public void Constructor_NullProviderId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelHealthSnapshot(null!, "model-1", ModelHealthStatus.Healthy));
    }

    [Fact]
    public void Constructor_NullModelId_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ModelHealthSnapshot(_defaultProviderId, null!, ModelHealthStatus.Healthy));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n")]
    public void Constructor_BlankModelId_ThrowsArgumentException(string blankModelId)
    {
        Assert.Throws<ArgumentException>(
            () => new ModelHealthSnapshot(_defaultProviderId, blankModelId, ModelHealthStatus.Healthy));
    }

    [Fact]
    public void Constructor_PreservesExactModelId()
    {
        string modelId = "gpt-4-turbo";
        ModelHealthSnapshot snapshot = new(_defaultProviderId, modelId, ModelHealthStatus.Healthy);

        Assert.Same(modelId, snapshot.ModelId);
    }

    [Fact]
    public void Constructor_PreservesLeadingAndTrailingSpacesOnNonWhitespaceModelId()
    {
        string modelId = "  gpt-4-turbo  ";
        ModelHealthSnapshot snapshot = new(_defaultProviderId, modelId, ModelHealthStatus.Healthy);

        Assert.Equal("  gpt-4-turbo  ", snapshot.ModelId);
        Assert.Same(modelId, snapshot.ModelId);
    }

    [Theory]
    [InlineData(ModelHealthStatus.Unknown)]
    [InlineData(ModelHealthStatus.Healthy)]
    [InlineData(ModelHealthStatus.Unhealthy)]
    public void Constructor_AcceptsDefinedStatus(ModelHealthStatus status)
    {
        ModelHealthSnapshot snapshot = new(_defaultProviderId, "model-1", status);

        Assert.Equal(status, snapshot.Status);
    }

    [Theory]
    [InlineData((ModelHealthStatus)(-1))]
    [InlineData((ModelHealthStatus)999)]
    public void Constructor_UndefinedStatus_ThrowsArgumentOutOfRangeException(ModelHealthStatus status)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelHealthSnapshot(_defaultProviderId, "model-1", status));
    }

    [Fact]
    public void PublicProperties_AreOnlyProviderIdModelIdAndStatus()
    {
        PropertyInfo[] publicProperties = typeof(ModelHealthSnapshot)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        string[] propertyNames = publicProperties
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        string[] expectedPropertyNames =
        [
            "ModelId",
            "ProviderId",
            "Status"
        ];

        Assert.Equal(expectedPropertyNames, propertyNames);
    }
}
