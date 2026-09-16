namespace AiRepoKit.Agents.Abstractions.Tests;

using AiRepoKit.Agents;
using Xunit;

public sealed class ExecutionPermissionTests
{
    [Fact]
    public void ReadOnly_HasValueOne()
    {
        Assert.Equal(
            1,
            (int)ExecutionPermission.ReadOnly);
    }

    [Fact]
    public void WorkspaceWrite_HasValueTwo()
    {
        Assert.Equal(
            2,
            (int)ExecutionPermission.WorkspaceWrite);
    }

    [Fact]
    public void Unrestricted_HasValueThree()
    {
        Assert.Equal(
            3,
            (int)ExecutionPermission.Unrestricted);
    }

    [Fact]
    public void Enum_ContainsExactlyThreeMembers()
    {
        string[] names =
            Enum.GetNames<ExecutionPermission>();

        Assert.Equal(
            3,
            names.Length);

        string[] expectedNames =
        [
            "ReadOnly",
            "WorkspaceWrite",
            "Unrestricted"
        ];

        Assert.Equal(
            expectedNames,
            names);
    }

    [Fact]
    public void Enum_ZeroIsNotDefined()
    {
        Assert.False(
            Enum.IsDefined(
                typeof(ExecutionPermission),
                0));

        Assert.False(
            Enum.IsDefined(
                (ExecutionPermission)0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(99)]
    public void Enum_ArbitraryInvalidNumericValuesAreNotDefined(
        int invalidValue_)
    {
        Assert.False(
            Enum.IsDefined(
                typeof(ExecutionPermission),
                invalidValue_));

        Assert.False(
            Enum.IsDefined(
                (ExecutionPermission)invalidValue_));
    }
}
