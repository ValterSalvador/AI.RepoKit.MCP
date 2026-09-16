using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

namespace AiRepoKit.Agents.Abstractions.Tests;

public sealed class AgentExecutionRequestTests
{
    private static readonly ExecutionEnvironment _defaultEnvironment =
        new(AppContext.BaseDirectory);

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
                    invalidInstruction_!,
                    ExecutionPermission.ReadOnly,
                    _defaultEnvironment));
    }

    [Fact]
    public void Constructor_PreservesExactInstructionValue()
    {
        string instruction =
            "  Implement step 1 without mutations.  ";

        AgentExecutionRequest request =
            new(
                instruction,
                ExecutionPermission.ReadOnly,
                _defaultEnvironment);

        Assert.Equal(
            instruction,
            request.Instruction);

        Assert.Null(
            request.SessionReference);

        Assert.Null(
            request.StructuredOutput);
    }

    [Fact]
    public void Constructor_RequiresExplicitPermissionAndEnvironment()
    {
        ConstructorInfo[] constructors =
            typeof(AgentExecutionRequest).GetConstructors();

        Assert.Single(
            constructors);

        ConstructorInfo constructor =
            constructors[0];

        ParameterInfo[] parameters =
            constructor.GetParameters();

        Assert.Equal(
            5,
            parameters.Length);

        Assert.Equal(
            "instruction_",
            parameters[0].Name);
        Assert.False(
            parameters[0].HasDefaultValue);

        Assert.Equal(
            "permission_",
            parameters[1].Name);
        Assert.Equal(
            typeof(ExecutionPermission),
            parameters[1].ParameterType);
        Assert.False(
            parameters[1].HasDefaultValue);

        Assert.Equal(
            "environment_",
            parameters[2].Name);
        Assert.Equal(
            typeof(ExecutionEnvironment),
            parameters[2].ParameterType);
        Assert.False(
            parameters[2].HasDefaultValue);

        Assert.Equal(
            "sessionReference_",
            parameters[3].Name);
        Assert.True(
            parameters[3].HasDefaultValue);
        Assert.Null(
            parameters[3].DefaultValue);

        Assert.Equal(
            "structuredOutput_",
            parameters[4].Name);
        Assert.True(
            parameters[4].HasDefaultValue);
        Assert.Null(
            parameters[4].DefaultValue);
    }

    [Theory]
    [InlineData(ExecutionPermission.ReadOnly)]
    [InlineData(ExecutionPermission.WorkspaceWrite)]
    [InlineData(ExecutionPermission.Unrestricted)]
    public void Constructor_PreservesExplicitPermissionAndEnvironment(
        ExecutionPermission permission_)
    {
        ExecutionEnvironment environment =
            new(AppContext.BaseDirectory);

        AgentExecutionRequest request =
            new(
                "Run review",
                permission_,
                environment);

        Assert.Equal(
            permission_,
            request.Permission);

        Assert.Same(
            environment,
            request.Environment);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void Constructor_RejectsInvalidPermissionValue(
        int invalidPermissionValue_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new AgentExecutionRequest(
                    "Valid instruction",
                    (ExecutionPermission)invalidPermissionValue_,
                    _defaultEnvironment));
    }

    [Fact]
    public void Constructor_RejectsNullEnvironment()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                new AgentExecutionRequest(
                    "Valid instruction",
                    ExecutionPermission.ReadOnly,
                    null!));
    }

    [Fact]
    public void Constructor_SupportsOptionalSessionReference()
    {
        AgentSessionReference sessionRef =
            new("session-abc");

        AgentExecutionRequest request =
            new(
                "Run review",
                ExecutionPermission.WorkspaceWrite,
                _defaultEnvironment,
                sessionRef);

        Assert.Equal(
            "Run review",
            request.Instruction);

        Assert.Equal(
            ExecutionPermission.WorkspaceWrite,
            request.Permission);

        Assert.NotNull(
            request.SessionReference);

        Assert.Equal(
            sessionRef,
            request.SessionReference);

        Assert.Null(
            request.StructuredOutput);
    }

    [Fact]
    public void Constructor_SupportsOptionalStructuredOutput()
    {
        StructuredOutputContract structuredOutput =
            new("{\"type\":\"object\"}");

        AgentExecutionRequest request =
            new(
                "Run structured query",
                ExecutionPermission.ReadOnly,
                _defaultEnvironment,
                structuredOutput_: structuredOutput);

        Assert.Equal(
            "Run structured query",
            request.Instruction);

        Assert.Null(
            request.SessionReference);

        Assert.NotNull(
            request.StructuredOutput);

        Assert.Same(
            structuredOutput,
            request.StructuredOutput);
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
            "Environment",
            "Instruction",
            "Permission",
            "SessionReference",
            "StructuredOutput"
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
    public void OldConstructorWithoutPermissionAndEnvironment_IsAbsent()
    {
        ConstructorInfo[] constructors =
            typeof(AgentExecutionRequest).GetConstructors();

        Assert.Single(
            constructors);

        foreach (ConstructorInfo ctor in constructors)
        {
            ParameterInfo[] parameters =
                ctor.GetParameters();

            Assert.True(
                parameters.Length >= 3,
                "Request constructor must require instruction, permission, and environment.");

            Assert.Contains(
                parameters,
                p_ =>
                    p_.Name == "permission_" &&
                    p_.ParameterType == typeof(ExecutionPermission) &&
                    !p_.HasDefaultValue);

            Assert.Contains(
                parameters,
                p_ =>
                    p_.Name == "environment_" &&
                    p_.ParameterType == typeof(ExecutionEnvironment) &&
                    !p_.HasDefaultValue);
        }
    }

    [Fact]
    public void Record_ImplementsValueEquality()
    {
        ExecutionEnvironment env1 =
            new(AppContext.BaseDirectory);
        ExecutionEnvironment env2 =
            new(AppContext.BaseDirectory);

        StructuredOutputContract output1 =
            new("{\"type\":\"object\"}");
        StructuredOutputContract output2 =
            new("{\"type\":\"object\"}");

        AgentExecutionRequest r1 =
            new(
                "Execute task",
                ExecutionPermission.ReadOnly,
                env1,
                new AgentSessionReference("s-1"),
                output1);

        AgentExecutionRequest r2 =
            new(
                "Execute task",
                ExecutionPermission.ReadOnly,
                env2,
                new AgentSessionReference("s-1"),
                output2);

        AgentExecutionRequest r3 =
            new(
                "Execute task",
                ExecutionPermission.WorkspaceWrite,
                env1,
                new AgentSessionReference("s-1"),
                output1);

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
