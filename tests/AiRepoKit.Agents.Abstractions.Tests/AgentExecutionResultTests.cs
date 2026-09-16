using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentExecutionResultTests
{
    [Fact]
    public void Status_HasExactNumericValues()
    {
        Assert.Equal(
            1,
            (int)AgentExecutionStatus.Completed);
        Assert.Equal(
            2,
            (int)AgentExecutionStatus.Blocked);
        Assert.Equal(
            3,
            (int)AgentExecutionStatus.NeedsInput);
        Assert.Equal(
            4,
            (int)AgentExecutionStatus.Failed);
    }

    [Fact]
    public void Status_DoesNotContainCancellation()
    {
        Assert.False(
            Enum.IsDefined(
                typeof(AgentExecutionStatus),
                "Canceled"));
        Assert.False(
            Enum.IsDefined(
                typeof(AgentExecutionStatus),
                "Cancelled"));
    }

    [Fact]
    public void Completed_FactorySetsExpectedProperties()
    {
        AgentSessionReference sessionRef =
            new("session-1");

        AgentExecutionResult result =
            AgentExecutionResult.Completed(
                outputText_: "Files analyzed successfully.",
                sessionReference_: sessionRef,
                diagnosticText_: "Diagnostic info");

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);
        Assert.Equal(
            "Files analyzed successfully.",
            result.OutputText);
        Assert.Equal(
            sessionRef,
            result.SessionReference);
        Assert.Equal(
            "Diagnostic info",
            result.DiagnosticText);
    }

    [Fact]
    public void Blocked_FactorySetsExpectedProperties()
    {
        AgentExecutionResult result =
            AgentExecutionResult.Blocked(
                diagnosticText_: "Execution requires file write permission.");

        Assert.Equal(
            AgentExecutionStatus.Blocked,
            result.Status);
        Assert.Equal(
            "Execution requires file write permission.",
            result.DiagnosticText);
        Assert.Null(
            result.OutputText);
        Assert.Null(
            result.SessionReference);
    }

    [Fact]
    public void NeedsInput_FactorySetsExpectedProperties()
    {
        AgentExecutionResult result =
            AgentExecutionResult.NeedsInput(
                diagnosticText_: "Please specify the target framework.");

        Assert.Equal(
            AgentExecutionStatus.NeedsInput,
            result.Status);
        Assert.Equal(
            "Please specify the target framework.",
            result.DiagnosticText);
        Assert.Null(
            result.OutputText);
        Assert.Null(
            result.SessionReference);
    }

    [Fact]
    public void Failed_FactorySetsExpectedProperties()
    {
        AgentExecutionResult result =
            AgentExecutionResult.Failed(
                diagnosticText_: "Process exited with error code 1.");

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Equal(
            "Process exited with error code 1.",
            result.DiagnosticText);
        Assert.Null(
            result.OutputText);
        Assert.Null(
            result.SessionReference);
    }

    [Fact]
    public void Result_ContainsOnlyAuthorizedProperties()
    {
        PropertyInfo[] properties =
            typeof(AgentExecutionResult).GetProperties(
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
            "DiagnosticText",
            "OutputText",
            "SessionReference",
            "Status"
        ];

        Assert.Equal(
            expectedPropertyNames,
            propertyNames);
    }

    [Theory]
    [InlineData("ProviderId")]
    [InlineData("TargetProvider")]
    [InlineData("RequestId")]
    [InlineData("ExitCode")]
    [InlineData("ProcessId")]
    [InlineData("TokenUsage")]
    [InlineData("Cost")]
    [InlineData("Telemetry")]
    public void Result_DoesNotContainProviderIdOrDeferredConcepts(
        string forbiddenPropertyName_)
    {
        PropertyInfo? property =
            typeof(AgentExecutionResult).GetProperty(
                forbiddenPropertyName_,
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.Static);

        Assert.Null(
            property);
    }

    [Fact]
    public void Result_CannotBeConstructedWithArbitraryOrInvalidEnumViaPublicApi()
    {
        ConstructorInfo[] publicConstructors =
            typeof(AgentExecutionResult).GetConstructors(
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.Empty(
            publicConstructors);
    }

    [Fact]
    public void Record_ImplementsValueEquality()
    {
        AgentExecutionResult r1 =
            AgentExecutionResult.Completed(
                "Output",
                new AgentSessionReference("s-1"));

        AgentExecutionResult r2 =
            AgentExecutionResult.Completed(
                "Output",
                new AgentSessionReference("s-1"));

        AgentExecutionResult r3 =
            AgentExecutionResult.Completed(
                "Different Output",
                new AgentSessionReference("s-1"));

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
