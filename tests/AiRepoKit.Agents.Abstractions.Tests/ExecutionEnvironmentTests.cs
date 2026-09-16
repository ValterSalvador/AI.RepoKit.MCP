namespace AiRepoKit.Agents.Abstractions.Tests;

using System.Reflection;
using AiRepoKit.Agents;
using Xunit;

public sealed class ExecutionEnvironmentTests
{
    [Fact]
    public void Type_IsReferenceTypeAndSealed()
    {
        Assert.False(
            typeof(ExecutionEnvironment).IsValueType);

        Assert.True(
            typeof(ExecutionEnvironment).IsClass);

        Assert.True(
            typeof(ExecutionEnvironment).IsSealed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" \r\n ")]
    public void Constructor_RejectsNullEmptyOrWhitespace(
        string? invalidPath_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new ExecutionEnvironment(
                    invalidPath_!));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("relative/path")]
    [InlineData("./relative/path")]
    [InlineData("../relative/path")]
    [InlineData("sub\\path")]
    public void Constructor_RejectsRelativePath(
        string relativePath_)
    {
        Assert.ThrowsAny<ArgumentException>(
            () =>
                new ExecutionEnvironment(
                    relativePath_));
    }

    [Fact]
    public void Constructor_AcceptsFullyQualifiedPath_AndPreservesExactSuppliedPath()
    {
        string fullyQualifiedPath =
            AppContext.BaseDirectory;

        ExecutionEnvironment environment =
            new(fullyQualifiedPath);

        Assert.Equal(
            fullyQualifiedPath,
            environment.WorkingDirectory);
    }

    [Fact]
    public void Constructor_DoesNotTrimWhitespaceFromSuppliedPath()
    {
        string pathWithSpace =
            OperatingSystem.IsWindows()
                ? @"C:\path\sub "
                : "/path/sub ";

        ExecutionEnvironment environment =
            new(pathWithSpace);

        Assert.Equal(
            pathWithSpace,
            environment.WorkingDirectory);

        Assert.EndsWith(
            " ",
            environment.WorkingDirectory);
    }

    [Fact]
    public void Constructor_DoesNotNormalizePathSeparatorsOrSegments()
    {
        string unnormalizedPath =
            OperatingSystem.IsWindows()
                ? "C:/foo/bar/../bar"
                : "/foo/bar/../bar";

        ExecutionEnvironment environment =
            new(unnormalizedPath);

        Assert.Equal(
            unnormalizedPath,
            environment.WorkingDirectory);

        Assert.Contains(
            '/',
            environment.WorkingDirectory);

        Assert.Contains(
            "..",
            environment.WorkingDirectory);
    }

    [Fact]
    public void Constructor_AcceptsNonexistentFullyQualifiedPath()
    {
        string nonexistentPath =
            Path.Combine(
                Path.GetTempPath(),
                $"nonexistent_{Guid.NewGuid():N}");

        Assert.False(
            Directory.Exists(
                nonexistentPath));

        Assert.False(
            File.Exists(
                nonexistentPath));

        ExecutionEnvironment environment =
            new(nonexistentPath);

        Assert.Equal(
            nonexistentPath,
            environment.WorkingDirectory);
    }

    [Fact]
    public void Equality_FollowsExactStoredWorkingDirectoryValue()
    {
        string path1 =
            OperatingSystem.IsWindows()
                ? @"C:\repo\workspace"
                : "/repo/workspace";

        string path2 =
            OperatingSystem.IsWindows()
                ? @"C:\repo\workspace"
                : "/repo/workspace";

        string path3 =
            OperatingSystem.IsWindows()
                ? @"C:\repo\other"
                : "/repo/other";

        ExecutionEnvironment env1 =
            new(path1);
        ExecutionEnvironment env2 =
            new(path2);
        ExecutionEnvironment env3 =
            new(path3);

        Assert.Equal(
            env1,
            env2);

        Assert.True(
            env1 == env2);

        Assert.False(
            env1 != env2);

        Assert.Equal(
            env1.GetHashCode(),
            env2.GetHashCode());

        Assert.NotEqual(
            env1,
            env3);

        Assert.False(
            env1 == env3);

        Assert.True(
            env1 != env3);
    }

    [Fact]
    public void PublicSurface_ContainsOnlyWorkingDirectoryProperty()
    {
        PropertyInfo[] properties =
            typeof(ExecutionEnvironment).GetProperties(
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.Single(
            properties);

        Assert.Equal(
            "WorkingDirectory",
            properties[0].Name);

        Assert.Equal(
            typeof(string),
            properties[0].PropertyType);

        FieldInfo[] fields =
            typeof(ExecutionEnvironment).GetFields(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static);

        Assert.Empty(
            fields);
    }
}
