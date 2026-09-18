namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ProcessExecutionRequestTests
{
    private static readonly string ValidWorkingDirectory =
        Path.GetFullPath(AppContext.BaseDirectory);

    [Fact]
    public void Constructor_NullExecutable_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProcessExecutionRequest(null!, [], ValidWorkingDirectory));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t\r\n")]
    public void Constructor_BlankExecutable_ThrowsArgumentException(string blankExecutable)
    {
        Assert.Throws<ArgumentException>(
            () => new ProcessExecutionRequest(blankExecutable, [], ValidWorkingDirectory));
    }

    [Fact]
    public void Constructor_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProcessExecutionRequest("dotnet", null!, ValidWorkingDirectory));
    }

    [Fact]
    public void Constructor_NullWorkingDirectory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProcessExecutionRequest("dotnet", [], null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("  \t\r\n")]
    public void Constructor_BlankWorkingDirectory_ThrowsArgumentException(string blankDir)
    {
        Assert.Throws<ArgumentException>(
            () => new ProcessExecutionRequest("dotnet", [], blankDir));
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("./relative")]
    [InlineData("../parent")]
    [InlineData("subfolder")]
    public void Constructor_NonFullyQualifiedWorkingDirectory_ThrowsArgumentException(string relativeDir)
    {
        Assert.Throws<ArgumentException>(
            () => new ProcessExecutionRequest("dotnet", [], relativeDir));
    }

    [Fact]
    public void Constructor_PreservesArgumentOrder()
    {
        List<string> args = ["first", "second", "third", "fourth"];
        ProcessExecutionRequest request = new("dotnet", args, ValidWorkingDirectory);

        Assert.Equal(4, request.Arguments.Count);
        Assert.Equal("first", request.Arguments[0]);
        Assert.Equal("second", request.Arguments[1]);
        Assert.Equal("third", request.Arguments[2]);
        Assert.Equal("fourth", request.Arguments[3]);
    }

    [Fact]
    public void Constructor_PreservesArgumentText_WithSpacesQuotesNewlines()
    {
        string complexArg = "arg with spaces and \"quotes\" and \r\n newlines \t tabs";
        List<string> args = [complexArg];
        ProcessExecutionRequest request = new("dotnet", args, ValidWorkingDirectory);

        Assert.Single(request.Arguments);
        Assert.Equal(complexArg, request.Arguments[0]);
    }

    [Fact]
    public void Constructor_DefensivelySnapshotsArguments()
    {
        List<string> originalArgs = ["initial1", "initial2"];
        ProcessExecutionRequest request = new("dotnet", originalArgs, ValidWorkingDirectory);

        originalArgs.Add("mutated");
        originalArgs[0] = "altered";

        Assert.Equal(2, request.Arguments.Count);
        Assert.Equal("initial1", request.Arguments[0]);
        Assert.Equal("initial2", request.Arguments[1]);
    }

    [Fact]
    public void Constructor_PreservesExactWorkingDirectoryString_WithoutNormalization()
    {
        // Path with trailing slash or specific casing must be preserved exactly
        string exactDir = ValidWorkingDirectory;
        ProcessExecutionRequest request = new("dotnet", [], exactDir);

        Assert.Same(exactDir, request.WorkingDirectory);
    }

    [Fact]
    public void Constructor_DoesNotProbeFileSystemExistence()
    {
        // Pass a fully qualified non-existent path
        string nonExistentPath = Path.Combine(
            Path.GetPathRoot(ValidWorkingDirectory)!,
            "non_existent_folder_abc123_xyz789");

        ProcessExecutionRequest request = new("dotnet", [], nonExistentPath);
        Assert.Equal(nonExistentPath, request.WorkingDirectory);
    }

    [Fact]
    public void Properties_ReturnExpectedValues()
    {
        ProcessExecutionRequest request = new("my_cli", ["--flag", "val"], ValidWorkingDirectory);

        Assert.Equal("my_cli", request.Executable);
        Assert.Equal(2, request.Arguments.Count);
        Assert.Equal(ValidWorkingDirectory, request.WorkingDirectory);
    }

    [Fact]
    public void Constructor_WithoutTimeout_DefaultsToNull()
    {
        ProcessExecutionRequest request = new("dotnet", ["--version"], ValidWorkingDirectory);
        Assert.Null(request.Timeout);
    }

    [Fact]
    public void Constructor_ExplicitNullTimeout_Accepted()
    {
        ProcessExecutionRequest request = new("dotnet", ["--version"], ValidWorkingDirectory, timeout_: null);
        Assert.Null(request.Timeout);
    }

    [Fact]
    public void Constructor_PositiveTimeout_RetainsExactValue()
    {
        TimeSpan timeout = TimeSpan.FromSeconds(42.5);
        ProcessExecutionRequest request = new("dotnet", ["--version"], ValidWorkingDirectory, timeout);
        Assert.Equal(timeout, request.Timeout);
    }

    [Fact]
    public void Constructor_ZeroTimeout_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessExecutionRequest("dotnet", ["--version"], ValidWorkingDirectory, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1000)]
    public void Constructor_NegativeTimeout_ThrowsArgumentOutOfRangeException(int negativeMs)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessExecutionRequest("dotnet", ["--version"], ValidWorkingDirectory, TimeSpan.FromMilliseconds(negativeMs)));
    }

    [Fact]
    public void Constructor_UpperBoundaryTimeout_Accepted()
    {
        TimeSpan boundaryTimeout = TimeSpan.FromMilliseconds(4294967294L);
        ProcessExecutionRequest request = new("dotnet", ["--version"], ValidWorkingDirectory, boundaryTimeout);
        Assert.Equal(boundaryTimeout, request.Timeout);
    }

    [Fact]
    public void Constructor_AboveUpperBoundaryTimeout_ThrowsArgumentOutOfRangeException()
    {
        TimeSpan aboveBoundary = TimeSpan.FromMilliseconds(4294967295L);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessExecutionRequest("dotnet", ["--version"], ValidWorkingDirectory, aboveBoundary));
    }

    [Fact]
    public void Constructor_TimeSpanMaxValue_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProcessExecutionRequest("dotnet", ["--version"], ValidWorkingDirectory, TimeSpan.MaxValue));
    }
}
