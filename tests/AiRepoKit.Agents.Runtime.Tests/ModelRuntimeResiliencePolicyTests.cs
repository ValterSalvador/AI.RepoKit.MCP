namespace AiRepoKit.Agents.Runtime.Tests;

using System.Reflection;
using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ModelRuntimeResiliencePolicyTests
{
    [Fact]
    public void Constructor_MaxAttemptsOne_Succeeds()
    {
        ModelRuntimeResiliencePolicy policy = new(1, TimeSpan.Zero);

        Assert.Equal(1, policy.MaxAttemptsPerCandidate);
        Assert.Equal(TimeSpan.Zero, policy.RetryBackoff);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    public void Constructor_MaxAttemptsGreaterThanOne_Succeeds(int maxAttempts_)
    {
        ModelRuntimeResiliencePolicy policy = new(maxAttempts_, TimeSpan.FromMilliseconds(500));

        Assert.Equal(maxAttempts_, policy.MaxAttemptsPerCandidate);
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.RetryBackoff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Constructor_MaxAttemptsZeroOrNegative_ThrowsArgumentOutOfRangeException(int invalidAttempts_)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelRuntimeResiliencePolicy(invalidAttempts_, TimeSpan.Zero));

        Assert.Equal("maxAttemptsPerCandidate_", exception.ParamName);
    }

    [Fact]
    public void Constructor_ZeroRetryBackoff_Succeeds()
    {
        ModelRuntimeResiliencePolicy policy = new(1, TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, policy.RetryBackoff);
    }

    [Fact]
    public void Constructor_PositiveRetryBackoff_Retained()
    {
        TimeSpan backoff = TimeSpan.FromSeconds(2.5);
        ModelRuntimeResiliencePolicy policy = new(2, backoff);

        Assert.Equal(backoff, policy.RetryBackoff);
    }

    [Fact]
    public void Constructor_NegativeRetryBackoff_ThrowsArgumentOutOfRangeException()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelRuntimeResiliencePolicy(1, TimeSpan.FromMilliseconds(-1)));

        Assert.Equal("retryBackoff_", exception.ParamName);
    }

    [Fact]
    public void Constructor_Exactly4294967294MillisecondsBackoff_Succeeds()
    {
        TimeSpan backoff = TimeSpan.FromMilliseconds(4294967294.0);
        ModelRuntimeResiliencePolicy policy = new(1, backoff);

        Assert.Equal(backoff, policy.RetryBackoff);
    }

    [Fact]
    public void Constructor_Above4294967294MillisecondsBackoff_ThrowsArgumentOutOfRangeException()
    {
        TimeSpan backoff = TimeSpan.FromMilliseconds(4294967295.0);
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModelRuntimeResiliencePolicy(1, backoff));

        Assert.Equal("retryBackoff_", exception.ParamName);
    }

    [Fact]
    public void Properties_HaveNoPublicSetterOrInit()
    {
        Type type = typeof(ModelRuntimeResiliencePolicy);

        PropertyInfo? maxAttemptsProp = type.GetProperty(nameof(ModelRuntimeResiliencePolicy.MaxAttemptsPerCandidate));
        Assert.NotNull(maxAttemptsProp);
        Assert.True(maxAttemptsProp.CanRead);
        Assert.Null(maxAttemptsProp.GetSetMethod());

        MethodInfo? maxAttemptsSetMethod = maxAttemptsProp.SetMethod;
        Assert.Null(maxAttemptsSetMethod);

        PropertyInfo? backoffProp = type.GetProperty(nameof(ModelRuntimeResiliencePolicy.RetryBackoff));
        Assert.NotNull(backoffProp);
        Assert.True(backoffProp.CanRead);
        Assert.Null(backoffProp.GetSetMethod());

        MethodInfo? backoffSetMethod = backoffProp.SetMethod;
        Assert.Null(backoffSetMethod);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        ModelRuntimeResiliencePolicy policyA = new(3, TimeSpan.FromSeconds(1));
        ModelRuntimeResiliencePolicy policyB = new(3, TimeSpan.FromSeconds(1));
        ModelRuntimeResiliencePolicy policyC = new(2, TimeSpan.FromSeconds(1));

        Assert.Equal(policyA, policyB);
        Assert.True(policyA == policyB);
        Assert.False(policyA == policyC);
        Assert.Equal(policyA.GetHashCode(), policyB.GetHashCode());
    }
}
