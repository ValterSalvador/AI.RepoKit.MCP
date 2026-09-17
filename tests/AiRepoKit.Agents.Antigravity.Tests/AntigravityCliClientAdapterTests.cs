using System.Diagnostics;
using AiRepoKit.Agents;
using AiRepoKit.Agents.Antigravity;
using Xunit;

namespace AiRepoKit.Agents.Antigravity.Tests;

public sealed class AntigravityCliClientAdapterTests
{
    private static ExecutionEnvironment CreateEnvironment()
    {
        return new ExecutionEnvironment(
            Path.GetFullPath(AppContext.BaseDirectory));
    }

    private static AgentExecutionRequest CreateRequest(
        string instruction_ = "Analyze repo",
        ExecutionPermission permission_ = ExecutionPermission.WorkspaceWrite,
        AgentSessionReference? sessionReference_ = null,
        StructuredOutputContract? structuredOutput_ = null)
    {
        return new AgentExecutionRequest(
            instruction_,
            permission_,
            CreateEnvironment(),
            sessionReference_,
            structuredOutput_);
    }

    // ---------------------------------------------------------
    // Provider identity and metadata (16-19)
    // ---------------------------------------------------------

    [Fact]
    public void ProviderId_HasExactValueAntigravity()
    {
        AntigravityCliClientAdapter adapter =
            new(new FakeAntigravityProcessRunner());

        Assert.Equal(
            "antigravity",
            adapter.ProviderId.Value);
    }

    [Fact]
    public void Capabilities_ContainsExactlyOneCapability()
    {
        AntigravityCliClientAdapter adapter =
            new(new FakeAntigravityProcessRunner());

        Assert.Single(
            adapter.Capabilities);
    }

    [Fact]
    public void Capabilities_ContainsExactCapabilityStructuredOutput()
    {
        AntigravityCliClientAdapter adapter =
            new(new FakeAntigravityProcessRunner());

        AgentCapability capability =
            new("structured-output");

        Assert.True(
            adapter.Capabilities.Supports(capability));
    }

    [Fact]
    public void Capabilities_EnumerationIsDeterministic()
    {
        AntigravityCliClientAdapter adapter =
            new(new FakeAntigravityProcessRunner());

        List<string> capabilityValues =
            adapter.Capabilities
                .Select(c_ => c_.Value)
                .ToList();

        Assert.Single(capabilityValues);
        Assert.Equal(
            "structured-output",
            capabilityValues[0]);
    }

    [Fact]
    public void PublicParameterlessConstructor_InstantiatesAdapter()
    {
        AntigravityCliClientAdapter adapter =
            new();

        Assert.Equal(
            "antigravity",
            adapter.ProviderId.Value);
        Assert.Single(
            adapter.Capabilities);
    }

    // ---------------------------------------------------------
    // Base invocation (20-30)
    // ---------------------------------------------------------

    [Fact]
    public void Invocation_ExecutableIsExactlyAgy()
    {
        AgentExecutionRequest request =
            CreateRequest();

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            "agy",
            invocation.Executable);
    }

    [Fact]
    public void Invocation_WorkingDirectoryIsExactEnvironmentValue()
    {
        ExecutionEnvironment env =
            CreateEnvironment();
        AgentExecutionRequest request =
            new(
                "Do task",
                ExecutionPermission.WorkspaceWrite,
                env);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            env.WorkingDirectory,
            invocation.WorkingDirectory);
    }

    [Fact]
    public void Invocation_FirstArgumentsAreExactFrozenBaseInvocation()
    {
        AgentExecutionRequest request =
            CreateRequest("Run tests now");

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.True(
            invocation.Arguments.Count >= 4);
        Assert.Equal(
            "-p",
            invocation.Arguments[0]);
        Assert.Equal(
            "Run tests now",
            invocation.Arguments[1]);
        Assert.Equal(
            "--output-format",
            invocation.Arguments[2]);
        Assert.Equal(
            "json",
            invocation.Arguments[3]);
    }

    [Fact]
    public void Invocation_InstructionWithSpaces_RemainsOneArgument()
    {
        string instruction =
            "Execute task with spaces in the middle and trailing words";

        AgentExecutionRequest request =
            CreateRequest(instruction);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            instruction,
            invocation.Arguments[1]);
    }

    [Fact]
    public void Invocation_InstructionWithQuotes_RemainsOneArgument()
    {
        string instruction =
            "Analyze \"quotes\" and 'single' and `backticks`";

        AgentExecutionRequest request =
            CreateRequest(instruction);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            instruction,
            invocation.Arguments[1]);
    }

    [Fact]
    public void Invocation_InstructionWithNewlines_RemainsOneArgument()
    {
        string instruction =
            "Line 1\r\nLine 2\nLine 3\n\nEnd";

        AgentExecutionRequest request =
            CreateRequest(instruction);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Equal(
            instruction,
            invocation.Arguments[1]);
    }

    [Fact]
    public void RealInvocationBuilder_ProducesValidProcessStartInfo()
    {
        AgentExecutionRequest request =
            CreateRequest("Verify build info");

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        ProcessStartInfo startInfo =
            AntigravityProcessRunner.CreateProcessStartInfo(invocation);

        Assert.Equal(
            "agy",
            startInfo.FileName);
        Assert.Equal(
            request.Environment.WorkingDirectory,
            startInfo.WorkingDirectory);
        Assert.False(
            startInfo.UseShellExecute);
        Assert.True(
            startInfo.RedirectStandardOutput);
        Assert.True(
            startInfo.RedirectStandardError);
        Assert.True(
            startInfo.CreateNoWindow);

        Assert.Equal(
            invocation.Arguments.Count,
            startInfo.ArgumentList.Count);

        for (int i = 0; i < invocation.Arguments.Count; i++)
        {
            Assert.Equal(
                invocation.Arguments[i],
                startInfo.ArgumentList[i]);
        }
    }

    // ---------------------------------------------------------
    // Permission mapping (31-38)
    // ---------------------------------------------------------

    [Fact]
    public async Task ReadOnly_ReturnsBlocked()
    {
        FakeAntigravityProcessRunner fakeRunner =
            new();
        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.ReadOnly);

        AgentExecutionResult result =
            await adapter.ExecuteAsync(request);

        Assert.Equal(
            AgentExecutionStatus.Blocked,
            result.Status);
    }

    [Fact]
    public async Task ReadOnly_NeverInvokesProcessSeam()
    {
        FakeAntigravityProcessRunner fakeRunner =
            new();
        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.ReadOnly);

        await adapter.ExecuteAsync(request);

        Assert.Equal(
            0,
            fakeRunner.InvocationCount);
        Assert.Null(
            fakeRunner.LastInvocation);
    }

    [Fact]
    public async Task ReadOnly_DiagnosticIsDeterministic()
    {
        FakeAntigravityProcessRunner fakeRunner =
            new();
        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.ReadOnly);

        AgentExecutionResult result =
            await adapter.ExecuteAsync(request);

        Assert.Equal(
            "Current Antigravity headless execution cannot represent the requested read-only policy.",
            result.DiagnosticText);
    }

    [Fact]
    public void ReadOnly_ThrowsOnBuildInvocation_DoesNotUsePlanMode()
    {
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.ReadOnly);

        Assert.Throws<InvalidOperationException>(
            () => AntigravityCliClientAdapter.BuildInvocation(request));
    }

    [Fact]
    public void WorkspaceWrite_AddsSandboxFlag_AndNotUnrestrictedFlag()
    {
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.WorkspaceWrite);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Contains(
            "--sandbox",
            invocation.Arguments);
        Assert.DoesNotContain(
            "--dangerously-skip-permissions",
            invocation.Arguments);
    }

    [Fact]
    public void Unrestricted_AddsDangerouslySkipPermissionsFlag_AndNotSandboxFlag()
    {
        AgentExecutionRequest request =
            CreateRequest(
                permission_: ExecutionPermission.Unrestricted);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.Contains(
            "--dangerously-skip-permissions",
            invocation.Arguments);
        Assert.DoesNotContain(
            "--sandbox",
            invocation.Arguments);
    }

    // ---------------------------------------------------------
    // Session mapping (39-45)
    // ---------------------------------------------------------

    [Fact]
    public void Invocation_NullInputSession_EmitsNoConversationFlag()
    {
        AgentExecutionRequest request =
            CreateRequest(
                sessionReference_: null);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.DoesNotContain(
            "--conversation",
            invocation.Arguments);
    }

    [Fact]
    public void Invocation_SessionEmitsConversationFlag_WithExactValue()
    {
        AgentSessionReference session =
            new("sess-uuid-12345");

        AgentExecutionRequest request =
            CreateRequest(
                sessionReference_: session);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        int flagIndex =
            invocation.Arguments.ToList().IndexOf("--conversation");

        Assert.True(
            flagIndex >= 0);
        Assert.Equal(
            "sess-uuid-12345",
            invocation.Arguments[flagIndex + 1]);
    }

    [Fact]
    public void Invocation_SessionIsPreservedOpaque_NotTrimmedOrNormalized()
    {
        AgentSessionReference session =
            new("  opaque session value with spaces  ");

        AgentExecutionRequest request =
            CreateRequest(
                sessionReference_: session);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        int flagIndex =
            invocation.Arguments.ToList().IndexOf("--conversation");

        Assert.Equal(
            "  opaque session value with spaces  ",
            invocation.Arguments[flagIndex + 1]);
    }

    [Fact]
    public void Result_ReturnedConversationId_BecomesAgentSessionReference()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"done\",\"conversation_id\":\"conv-789\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(
                0,
                stdout,
                string.Empty);

        Assert.NotNull(result.SessionReference);
        Assert.Equal(
            "conv-789",
            result.SessionReference.Value);
    }

    [Fact]
    public void Result_ReturnedConversationId_ExactValuePreserved()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"done\",\"conversation_id\":\"  opaque-id  \"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(
                0,
                stdout,
                string.Empty);

        Assert.NotNull(result.SessionReference);
        Assert.Equal(
            "  opaque-id  ",
            result.SessionReference.Value);
    }

    [Fact]
    public void Result_AbsentConversationId_ProducesNullSessionReference()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"done\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(
                0,
                stdout,
                string.Empty);

        Assert.Null(result.SessionReference);
    }

    [Fact]
    public void Result_WhitespaceConversationId_ProducesNullSessionReference()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"done\",\"conversation_id\":\"   \"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(
                0,
                stdout,
                string.Empty);

        Assert.Null(result.SessionReference);
    }

    // ---------------------------------------------------------
    // Structured output mapping (46-49)
    // ---------------------------------------------------------

    [Fact]
    public void Invocation_NullStructuredOutput_EmitsNoJsonSchemaFlag()
    {
        AgentExecutionRequest request =
            CreateRequest(
                structuredOutput_: null);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        Assert.DoesNotContain(
            "--json-schema",
            invocation.Arguments);
    }

    [Fact]
    public void Invocation_StructuredOutput_EmitsJsonSchemaFlagAndExactValue()
    {
        string schema =
            "{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}}}";

        StructuredOutputContract contract =
            new(schema);

        AgentExecutionRequest request =
            CreateRequest(
                structuredOutput_: contract);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        int flagIndex =
            invocation.Arguments.ToList().IndexOf("--json-schema");

        Assert.True(
            flagIndex >= 0);
        Assert.Equal(
            schema,
            invocation.Arguments[flagIndex + 1]);
    }

    [Fact]
    public void Invocation_StructuredOutput_PreservesNonNormalizedSchemaFormatting()
    {
        string unnormalizedSchema =
            "{\n  \"type\": \"object\",\n  \"properties\": {\n    \"count\": { \"type\": \"integer\" }\n  }\n}";

        StructuredOutputContract contract =
            new(unnormalizedSchema);

        AgentExecutionRequest request =
            CreateRequest(
                structuredOutput_: contract);

        AntigravityProcessInvocation invocation =
            AntigravityCliClientAdapter.BuildInvocation(request);

        int flagIndex =
            invocation.Arguments.ToList().IndexOf("--json-schema");

        Assert.Equal(
            unnormalizedSchema,
            invocation.Arguments[flagIndex + 1]);
    }

    // ---------------------------------------------------------
    // Status mapping (50-60)
    // ---------------------------------------------------------

    [Fact]
    public void StatusMapping_Success_MapsToCompleted()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"Everything completed.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);
        Assert.Equal(
            "Everything completed.",
            result.OutputText);
    }

    [Fact]
    public void StatusMapping_Waiting_MapsToNeedsInput()
    {
        string stdout =
            "{\"status\":\"WAITING\",\"response\":\"Please provide approval for step 2.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.NeedsInput,
            result.Status);
        Assert.Equal(
            "Please provide approval for step 2.",
            result.OutputText);
    }

    [Fact]
    public void StatusMapping_Error_MapsToFailed()
    {
        string stdout =
            "{\"status\":\"ERROR\",\"error\":\"Something went wrong.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Equal(
            "Something went wrong.",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_Invalid_MapsToFailed()
    {
        string stdout =
            "{\"status\":\"INVALID\",\"error\":\"Invalid argument passed.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Equal(
            "Invalid argument passed.",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_Canceled_WithoutCallerCancellation_MapsToFailed()
    {
        string stdout =
            "{\"status\":\"CANCELED\",\"error\":\"Provider canceled execution.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Equal(
            "Provider canceled execution.",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_Interrupted_WithoutCallerCancellation_MapsToFailed()
    {
        string stdout =
            "{\"status\":\"INTERRUPTED\",\"error\":\"Provider execution was interrupted.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Equal(
            "Provider execution was interrupted.",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_Running_MapsToFailed()
    {
        string stdout =
            "{\"status\":\"RUNNING\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Equal(
            "Antigravity reported RUNNING.",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_UnknownStatus_MapsToFailed()
    {
        string stdout =
            "{\"status\":\"SOME_STRANGE_STATUS\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "SOME_STRANGE_STATUS",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_MissingStatus_MapsToFailed()
    {
        string stdout =
            "{\"response\":\"Got response but no status\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "missing status",
            result.DiagnosticText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StatusMapping_MalformedJson_MapsToFailed()
    {
        string stdout =
            "{ this is not valid json }";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "malformed JSON",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_EmptyStdout_MapsToFailed()
    {
        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, string.Empty, "some error");

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "empty stdout",
            result.DiagnosticText);
        Assert.Contains(
            "some error",
            result.DiagnosticText);
    }

    [Fact]
    public void StatusMapping_WhitespaceStdout_MapsToFailed()
    {
        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, "   \n\t  ", string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "empty stdout",
            result.DiagnosticText);
    }

    // ---------------------------------------------------------
    // Process exit semantics (61-63)
    // ---------------------------------------------------------

    [Fact]
    public void ExitSemantics_SuccessWithExitCode0_ReturnsCompleted()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"All good\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.Completed,
            result.Status);
        Assert.Equal(
            "All good",
            result.OutputText);
    }

    [Fact]
    public void ExitSemantics_SuccessWithNonzeroExitCode_ReturnsFailed()
    {
        string stdout =
            "{\"status\":\"SUCCESS\",\"response\":\"All good\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, "Process crashed unexpectedly");

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "code 1",
            result.DiagnosticText);
        Assert.Contains(
            "Process crashed unexpectedly",
            result.DiagnosticText);
    }

    [Fact]
    public void ExitSemantics_ProviderErrorWithNonzeroExitCode_RemainsFailed()
    {
        string stdout =
            "{\"status\":\"ERROR\",\"error\":\"Failed execution\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(2, stdout, "Process stderr info");

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Failed execution",
            result.DiagnosticText);
        Assert.Contains(
            "Process stderr info",
            result.DiagnosticText);
    }

    // ---------------------------------------------------------
    // Output and diagnostics mapping (64-68)
    // ---------------------------------------------------------

    [Fact]
    public void OutputMapping_ResponseMapsToExactOutputText()
    {
        string exactResponse =
            "Multi-line response\nwith \"quotes\" and\ttabs";
        string stdout =
            $"{{\"status\":\"SUCCESS\",\"response\":{System.Text.Json.JsonSerializer.Serialize(exactResponse)}}}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            exactResponse,
            result.OutputText);
    }

    [Fact]
    public void OutputMapping_WaitingResponsePreservedAsOutputText()
    {
        string waitingQuestion =
            "Do you want to proceed with deletion of file.txt?";
        string stdout =
            $"{{\"status\":\"WAITING\",\"response\":{System.Text.Json.JsonSerializer.Serialize(waitingQuestion)}}}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(0, stdout, string.Empty);

        Assert.Equal(
            AgentExecutionStatus.NeedsInput,
            result.Status);
        Assert.Equal(
            waitingQuestion,
            result.OutputText);
    }

    [Fact]
    public void DiagnosticMapping_ErrorMapsToDiagnosticText()
    {
        string stdout =
            "{\"status\":\"ERROR\",\"error\":\"Specific provider failure message.\"}";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, string.Empty);

        Assert.Equal(
            "Specific provider failure message.",
            result.DiagnosticText);
    }

    [Fact]
    public void DiagnosticMapping_StderrRetainedOnFailure()
    {
        string stdout =
            "{\"status\":\"ERROR\"}";
        string stderr =
            "stderr: crash dump line 1\nstderr: line 2";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, stderr);

        Assert.Equal(
            stderr,
            result.DiagnosticText);
    }

    [Fact]
    public void DiagnosticMapping_ProviderErrorAndStderrCombinedDeterministically()
    {
        string stdout =
            "{\"status\":\"ERROR\",\"error\":\"Provider fatal error.\"}";
        string stderr =
            "Native stderr trace.";

        AgentExecutionResult result =
            AntigravityCliClientAdapter.ParseExecutionResult(1, stdout, stderr);

        Assert.Equal(
            "Provider fatal error.\nNative stderr trace.",
            result.DiagnosticText);
    }

    // ---------------------------------------------------------
    // Caller cancellation (69-73)
    // ---------------------------------------------------------

    [Fact]
    public async Task Cancellation_PreCanceledToken_PropagatesOperationCanceledException()
    {
        FakeAntigravityProcessRunner fakeRunner =
            new();
        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        using CancellationTokenSource cts =
            new();
        cts.Cancel();

        AgentExecutionRequest request =
            CreateRequest();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(request, cts.Token));
    }

    [Fact]
    public async Task Cancellation_PreCanceledToken_DoesNotInvokeProcessSeam()
    {
        FakeAntigravityProcessRunner fakeRunner =
            new();
        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        using CancellationTokenSource cts =
            new();
        cts.Cancel();

        AgentExecutionRequest request =
            CreateRequest();

        try
        {
            await adapter.ExecuteAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(
            0,
            fakeRunner.InvocationCount);
    }

    [Fact]
    public async Task Cancellation_InFlightCallerCancellation_PropagatesOperationCanceledException()
    {
        using CancellationTokenSource cts =
            new();

        FakeAntigravityProcessRunner fakeRunner =
            new()
            {
                CustomHandler = async (inv_, token_) =>
                {
                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    await Task.Yield();
                    return new AntigravityProcessResult(0, "{}", string.Empty);
                }
            };

        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => adapter.ExecuteAsync(request, cts.Token));
    }

    [Fact]
    public async Task Cancellation_InFlightCancellation_InvokesProcessTerminationInFakeSeam()
    {
        using CancellationTokenSource cts =
            new();

        FakeAntigravityProcessRunner fakeRunner =
            new()
            {
                CustomHandler = async (inv_, token_) =>
                {
                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    await Task.Yield();
                    return new AntigravityProcessResult(0, "{}", string.Empty);
                }
            };

        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        try
        {
            await adapter.ExecuteAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.True(
            fakeRunner.TerminationInvoked,
            "Cancellation registration must invoke process termination.");
    }

    [Fact]
    public async Task Cancellation_CallerCancellationNeverReturnsFailed()
    {
        using CancellationTokenSource cts =
            new();

        FakeAntigravityProcessRunner fakeRunner =
            new()
            {
                CustomHandler = async (inv_, token_) =>
                {
                    cts.Cancel();
                    token_.ThrowIfCancellationRequested();
                    await Task.Yield();
                    return new AntigravityProcessResult(0, "{}", string.Empty);
                }
            };

        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        OperationCanceledException ex =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => adapter.ExecuteAsync(request, cts.Token));

        Assert.NotNull(ex);
    }

    // ---------------------------------------------------------
    // Process launch failures
    // ---------------------------------------------------------

    [Fact]
    public async Task ProcessStartFailure_MapsToFailedWithDiagnostic()
    {
        FakeAntigravityProcessRunner fakeRunner =
            new()
            {
                ExceptionToThrow =
                    new System.ComponentModel.Win32Exception(2, "The system cannot find the file specified")
            };

        AntigravityCliClientAdapter adapter =
            new(fakeRunner);

        AgentExecutionRequest request =
            CreateRequest();

        AgentExecutionResult result =
            await adapter.ExecuteAsync(request);

        Assert.Equal(
            AgentExecutionStatus.Failed,
            result.Status);
        Assert.Contains(
            "Failed to start or execute Antigravity process",
            result.DiagnosticText);
        Assert.Contains(
            "The system cannot find the file specified",
            result.DiagnosticText);
    }

    // ---------------------------------------------------------
    // Process runner cancellation and termination structure
    // ---------------------------------------------------------

    [Fact]
    public async Task ProcessRunner_TerminateProcessTreeAsync_ThrowsWhenProcessIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => AntigravityProcessRunner.TerminateProcessTreeAsync(null!));
    }

    [Fact]
    public async Task ProcessRunner_TerminateProcessTreeAsync_AlreadyExitedProcess_ReturnsPromptly()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = Environment.ProcessPath ?? "dotnet",
            Arguments = "--version",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
        Assert.True(process.HasExited);

        await AntigravityProcessRunner.TerminateProcessTreeAsync(process);
    }

    [Fact]
    public async Task ProcessRunner_RunAsync_PreCanceledToken_ThrowsOperationCanceledException()
    {
        AntigravityProcessRunner runner = new();
        AntigravityProcessInvocation invocation = new(
            "agy",
            [],
            AppContext.BaseDirectory);

        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(invocation, cts.Token));
    }
}
