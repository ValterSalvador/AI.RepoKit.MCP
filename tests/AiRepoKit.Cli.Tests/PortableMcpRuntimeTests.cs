using System.Reflection;
using AiRepoKit.Cli.McpRuntime;
using AiRepoKit.Cli.McpRuntime.Prompts;
using AiRepoKit.Cli.McpRuntime.Resources;
using AiRepoKit.Cli.McpRuntime.Services;
using AiRepoKit.Cli.McpRuntime.Tools;
using ModelContextProtocol.Server;
using Xunit;

namespace AiRepoKit.Cli.Tests;

public sealed class PortableMcpRuntimeTests
{
    [Fact]
    public void McpServeRepositoryResolver_ExplicitRepoPath_IsUsedExactly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "airepo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            (bool success, string repoRoot, string errorMessage) = McpServeRepositoryResolver.Resolve(tempDir);
            Assert.True(success);
            Assert.Equal(Path.GetFullPath(tempDir), repoRoot);
            Assert.Empty(errorMessage);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void McpServeRepositoryResolver_NonexistentExplicitRepo_Fails()
    {
        string nonexistent = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid().ToString("N"));
        (bool success, string repoRoot, string errorMessage) = McpServeRepositoryResolver.Resolve(nonexistent);
        Assert.False(success);
        Assert.Empty(repoRoot);
        Assert.Contains("does not exist", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void McpServeRepositoryResolver_NearestGitRoot_ResolvedWhenRepoOmitted()
    {
        string rootDir = Path.Combine(Path.GetTempPath(), "airepo-git-test-" + Guid.NewGuid().ToString("N"));
        string nestedDir = Path.Combine(rootDir, "src", "nested", "sub");
        Directory.CreateDirectory(nestedDir);
        Directory.CreateDirectory(Path.Combine(rootDir, ".git"));
        try
        {
            (bool success, string repoRoot, string errorMessage) = McpServeRepositoryResolver.Resolve(null, nestedDir);
            Assert.True(success);
            Assert.Equal(Path.GetFullPath(rootDir), repoRoot);
            Assert.Empty(errorMessage);
        }
        finally
        {
            Directory.Delete(rootDir, true);
        }
    }

    [Fact]
    public void McpServeRepositoryResolver_NoGitRoot_ProducesExplicitFailure()
    {
        string isolatedDir = Path.Combine(Path.GetTempPath(), "airepo-no-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDir);
        try
        {
            string? foundRoot = McpServeRepositoryResolver.FindNearestGitRoot(isolatedDir);
            Assert.Null(foundRoot);

            (bool success, string repoRoot, string errorMessage) = McpServeRepositoryResolver.Resolve(null, isolatedDir);
            Assert.False(success);
            Assert.Empty(repoRoot);
            Assert.Contains("--repo <path>", errorMessage);
        }
        finally
        {
            Directory.Delete(isolatedDir, true);
        }
    }

    [Fact]
    public async Task Program_RunMcpServeAsync_UnknownOptionFails()
    {
        int exitCode = await Program.RunMcpServeAsync(["mcp", "serve", "--unknown-flag"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Program_RunMcpServeAsync_MissingRepoValueFails()
    {
        int exitCode = await Program.RunMcpServeAsync(["mcp", "serve", "--repo"]);
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task Program_Main_McpServeDispatchBypassesNormalStdoutPath()
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        using StringWriter capturedOut = new();
        using StringWriter capturedError = new();
        int exitCode;
        try
        {
            Console.SetOut(capturedOut);
            Console.SetError(capturedError);
            exitCode = await Program.Main(["mcp", "serve", "--unknown-r01a-option"]);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        Assert.Equal(1, exitCode);
        Assert.Empty(capturedOut.ToString());
        Assert.Contains("--unknown-r01a-option", capturedError.ToString());
        Assert.DoesNotContain("# ", capturedOut.ToString());
    }

    [Fact]
    public void McpServerHost_DefaultLogFile_IsOutsideTargetRepository()
    {
        string targetRepo = Path.Combine(Path.GetTempPath(), "airepo-target-repo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetRepo);
        try
        {
            string defaultLogFile = McpServerHost.DefaultLogFile;
            string fullTargetRepo = Path.GetFullPath(targetRepo);
            string fullDefaultLogFile = Path.GetFullPath(defaultLogFile);

            Assert.False(
                fullDefaultLogFile.StartsWith(fullTargetRepo + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || fullDefaultLogFile.StartsWith(fullTargetRepo + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(targetRepo, true);
        }
    }

    [Fact]
    public void PortableRuntime_HasNoDependencyOnToolsAiContextMcpBin()
    {
        Assembly cliAssembly = typeof(McpServerHost).Assembly;
        AssemblyName[] referencedAssemblies = cliAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referencedAssemblies, a_ => a_.Name?.Contains("AiRepo.ContextMcp", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void Tools_ExpectedFiveToolsPreserved()
    {
        string[] expectedTools =
        [
            "get_repo_brief",
            "get_health",
            "get_policy",
            "get_context",
            "search_context"
        ];

        MethodInfo[] methods = typeof(RepositoryContextTools).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<string> toolNames = [];
        foreach (MethodInfo method in methods)
        {
            McpServerToolAttribute? attr = method.GetCustomAttribute<McpServerToolAttribute>();
            if (attr?.Name is not null)
            {
                toolNames.Add(attr.Name);
            }
        }

        toolNames.Sort(StringComparer.Ordinal);
        string[] sortedExpected = [.. expectedTools.Order(StringComparer.Ordinal)];

        Assert.Equal(sortedExpected, toolNames);
    }

    [Fact]
    public void Resources_ExpectedNineResourcesPreserved()
    {
        string[] expectedResources =
        [
            "repo://brief",
            "repo://health",
            "repo://policy",
            "repo://context/changed-files",
            "repo://context/review-risk",
            "repo://context/test-generation",
            "repo://graph/dependencies",
            "repo://impact/current",
            "repo://org/report"
        ];

        MethodInfo[] methods = typeof(RepositoryContextResources).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<string> resourceUris = [];
        foreach (MethodInfo method in methods)
        {
            McpServerResourceAttribute? attr = method.GetCustomAttribute<McpServerResourceAttribute>();
            if (attr?.UriTemplate is not null)
            {
                resourceUris.Add(attr.UriTemplate);
            }
        }

        resourceUris.Sort(StringComparer.Ordinal);
        string[] sortedExpected = [.. expectedResources.Order(StringComparer.Ordinal)];

        Assert.Equal(sortedExpected, resourceUris);
    }

    [Fact]
    public void Prompts_AllCurrentPromptsPreserved()
    {
        string[] expectedPrompts =
        [
            "ai-repo.help",
            "ai-repo.tutorial-en",
            "ai-repo.tutorial-pt",
            "ai-repo.token-efficiency-check",
            "ai-repo.review-risk",
            "ai-repo.changed-files-review",
            "ai-repo.generate-tests",
            "ai-repo.before-commit",
            "ai-repo.implementation-plan",
            "ai-repo.release-check",
            "ai-repo.workflow.feature-implementation",
            "ai-repo.workflow.bug-fix",
            "ai-repo.workflow.before-commit",
            "ai-repo.workflow.release-preparation",
            "ai-repo.workflow.test-generation",
            "ai-repo.workflow.architecture-review",
            "ai-repo.workflow.migration-planning"
        ];

        MethodInfo[] methods = typeof(RepositoryContextPrompts).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        List<string> promptNames = [];
        foreach (MethodInfo method in methods)
        {
            McpServerPromptAttribute? attr = method.GetCustomAttribute<McpServerPromptAttribute>();
            if (attr?.Name is not null)
            {
                promptNames.Add(attr.Name);
            }
        }

        promptNames.Sort(StringComparer.Ordinal);
        string[] sortedExpected = [.. expectedPrompts.Order(StringComparer.Ordinal)];

        Assert.Equal(sortedExpected, promptNames);
    }

    [Fact]
    public void TargetRepositoryServiceSurface_RemainsReadOnly()
    {
        SecretRedactor redactor = new();
        ContextRepository repository = new(new ContextRepositoryOptions(Path.GetTempPath()), redactor);
        dynamic policy = repository.GetPolicyObject("all");

        Assert.Equal("read-only", (string)policy.serverMode);
        Assert.False((bool)policy.fileWrite);
        Assert.False((bool)policy.commandExecution);
        Assert.False((bool)policy.databaseAccess);
        Assert.False((bool)policy.networkAccess);
        Assert.True((bool)policy.secretsRedaction);
    }
}
