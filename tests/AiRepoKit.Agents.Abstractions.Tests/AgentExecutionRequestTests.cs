using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentExecutionRequestTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" \r\n ")]
    public void Constructor_RejectsNullEmptyOrWhitespaceInstruction(
        string? invalidInstruction_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new AgentExecutionRequest(
                    invalidInstruction_!));
    }

    [Fact]
    public void Constructor_PreservesExactInstructionValue()
    {
        string instruction =
            "  Implement step 1 without mutations.  ";

        AgentExecutionRequest request =
            new(instruction);

        Assert.Equal(
            instruction,
            request.Instruction);

        Assert.Null(
            request.SessionReference);
    }

    [Fact]
    public void Constructor_SupportsOptionalSessionReference()
    {
        AgentSessionReference sessionRef =
            new("session-abc");

        AgentExecutionRequest request =
            new(
                "Run review",
                sessionRef);

        Assert.Equal(
            "Run review",
            request.Instruction);

        Assert.NotNull(
            request.SessionReference);

        Assert.Equal(
            sessionRef,
            request.SessionReference);
    }

    [Fact]
    public void Request_ContainsOnlyAuthorizedProperties()
    {
        PropertyInfo[] properties =
            typeof(AgentExecutionRequest).GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance);

        string[] propertyNames =
            properties
                .Select(
                    prop_ =>
                        prop_.Name)
                .OrderBy(
                    name_ =>
                        name_,
                    StringComparer.Ordinal)
                .ToArray();

        string[] expectedPropertyNames =
        [
            "Instruction",
            "SessionReference"
        ];

        Assert.Equal(
            expectedPropertyNames,
            propertyNames);
    }

    [Theory]
    [InlineData("RequestId")]
    [InlineData("TargetProvider")]
    [InlineData("ProviderId")]
    [InlineData("Capabilities")]
    [InlineData("Permissions")]
    [InlineData("Environment")]
    [InlineData("ExecutionEnvironment")]
    [InlineData("Timeout")]
    [InlineData("Model")]
    [InlineData("Retry")]
    [InlineData("Telemetry")]
    public void Request_DoesNotContainDeferredOrForbiddenConcepts(
        string forbiddenPropertyName_)
    {
        PropertyInfo? property =
            typeof(AgentExecutionRequest).GetProperty(
                forbiddenPropertyName_,
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static);

        Assert.Null(
            property);
    }

    [Fact]
    public void Record_ImplementsValueEquality()
    {
        AgentExecutionRequest r1 =
            new(
                "Execute task",
                new AgentSessionReference("s-1"));

        AgentExecutionRequest r2 =
            new(
                "Execute task",
                new AgentSessionReference("s-1"));

        AgentExecutionRequest r3 =
            new(
                "Execute task",
                new AgentSessionReference("s-2"));

        Assert.Equal(
            r1,
            r2);
        Assert.True(
            r1 == r2);
        Assert.False(
            r1 != r2);

        Assert.NotEqual(
            r1,
            r3);
    }
}
