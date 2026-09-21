namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelHealthStatusTests
{
    [Fact]
    public void NumericValues_AreExpected()
    {
        Assert.Equal(0, (int)ModelHealthStatus.Unknown);
        Assert.Equal(1, (int)ModelHealthStatus.Healthy);
        Assert.Equal(2, (int)ModelHealthStatus.Unhealthy);
    }

    [Fact]
    public void DefinedValues_CountIsExactlyThree()
    {
        ModelHealthStatus[] values = Enum.GetValues<ModelHealthStatus>();

        Assert.Equal(3, values.Length);
        Assert.Contains(ModelHealthStatus.Unknown, values);
        Assert.Contains(ModelHealthStatus.Healthy, values);
        Assert.Contains(ModelHealthStatus.Unhealthy, values);
    }
}
