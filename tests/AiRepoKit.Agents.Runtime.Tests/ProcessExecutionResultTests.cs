namespace AiRepoKit.Agents.Runtime.Tests;

using AiRepoKit.Agents.Runtime;
using Xunit;

public sealed class ProcessExecutionResultTests
{
    [Theory]
    [InlineData(-100)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(137)]
    [InlineData(255)]
    public void Constructor_AcceptsArbitraryExitCode(int exitCode)
    {
        ProcessExecutionResult result = new(exitCode, "out", "err");
        Assert.Equal(exitCode, result.ExitCode);
    }

    [Fact]
    public void Constructor_NullStandardOutput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProcessExecutionResult(0, null!, "err"));
    }

    [Fact]
    public void Constructor_NullStandardError_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ProcessExecutionResult(0, "out", null!));
    }

    [Fact]
    public void Constructor_PreservesExactStandardOutputAndError_WithoutTrimmingOrRedaction()
    {
        string exactStdout = "  \t\r\n stdout with exact whitespace and symbols <>&% \r\n  ";
        string exactStderr = "  \t\r\n stderr with exact whitespace and symbols \r\n  ";

        ProcessExecutionResult result = new(0, exactStdout, exactStderr);

        Assert.Same(exactStdout, result.StandardOutput);
        Assert.Same(exactStderr, result.StandardError);
    }

    [Fact]
    public void Constructor_EmptyStrings_Allowed()
    {
        ProcessExecutionResult result = new(0, string.Empty, string.Empty);

        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }
}
