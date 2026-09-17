# AI.RepoKit V4 Agent Execution

This document provides the human-facing technical reference guide for Agent Execution in AI.RepoKit V4.

---

## Scope

AI.RepoKit V4 introduces provider-neutral execution contracts and concrete CLI adapters for executing autonomous coding agents against target repositories:

- **Provider-neutral execution contracts**: Abstract definitions for agent requests, results, statuses, capabilities, permissions, and environment contexts defined in `AiRepoKit.Agents.Abstractions`.
- **Antigravity CLI materialization**: Concrete adapter in `AiRepoKit.Agents.Antigravity` invoking the `agy` CLI in headless mode.
- **Codex CLI materialization**: Concrete adapter in `AiRepoKit.Agents.Codex` invoking the `codex` CLI in headless exec mode.
- **Deterministic and offline validation**: Test suites using decoupled process runners to verify adapter arguments, output parsing, diagnostic handling, and cancellation without invoking real vendor CLIs or external APIs.
- **Windows and Linux CI acceptance**: Deterministic test gate running the full solution build and tests across `windows-2025` and `ubuntu-24.04`.

> [!IMPORTANT]
> **Agent execution is not workflow orchestration.**
> V4 focuses exclusively on executing a single prompt or instruction via an external agent CLI. V4 does not introduce DAG orchestration, multi-step agent plans, agent-to-agent collaboration, state machine sequencing, or workflow resume mechanisms. Workflow orchestration remains separate roadmap work.

---

## Architecture Boundary

The agent execution subsystem maintains a decoupled architecture where public domain contracts are strictly isolated from vendor CLIs and runtime abstractions:

```text
AiRepoKit.Agents.Abstractions
        |
        +--> AiRepoKit.Agents.Antigravity
        |
        +--> AiRepoKit.Agents.Codex
```

Key architectural principles:

- **Zero External Dependencies**: The contract library `AiRepoKit.Agents.Abstractions` has zero external `PackageReference` dependencies. It relies solely on standard .NET runtime libraries.
- **Independence from Spec Subsystem**: `AiRepoKit.Agents.Abstractions` does not reference `AiRepoKit.Spec` or any other internal project.
- **One-Way Dependency Flow**: Provider adapters (`AiRepoKit.Agents.Antigravity` and `AiRepoKit.Agents.Codex`) depend on `AiRepoKit.Agents.Abstractions`. The abstractions layer contains zero knowledge of vendor CLIs or specific providers.
- **CLI Flags Are Adapter Implementation Details**: Provider-specific CLI flags (such as `--sandbox`, `--dangerously-skip-permissions`, or `--output-schema`) are adapter mappings, never domain contracts.
- **No Third-Party Agent Framework Types**: No `Microsoft.Extensions.AI` or Microsoft Agent Framework types enter the V4 domain contracts.
- **No Generic Reusable Process Runtime**: Adapters contain dedicated, private process execution logic tailored to their respective CLI protocols. No general-purpose process runner framework is introduced in V4.

---

## Provider-Neutral Types

The frozen public contract surface in `AiRepoKit.Agents.Abstractions` consists of exactly 11 types:

| Type | Kind | Purpose |
| --- | --- | --- |
| `IAgentExecutor` | Interface | Core contract for an agent executor, exposing provider identity, capability metadata, and single-request execution. |
| `AgentExecutionRequest` | Sealed Record | Immutable input specification encapsulating the instruction, permission level, execution environment, optional session reference, and optional structured output contract. |
| `AgentExecutionResult` | Sealed Record | Immutable outcome of an execution containing terminal status, optional output text, diagnostic text, and optional session reference. |
| `AgentExecutionStatus` | Enum | Strongly typed terminal execution outcome: `Completed`, `Blocked`, `NeedsInput`, or `Failed`. |
| `AgentProviderId` | Sealed Record | Validated lowercase ASCII identifier (1–64 characters) identifying the agent provider (e.g., `antigravity`, `codex`). |
| `AgentCapability` | Sealed Record | Validated lowercase ASCII identifier (1–64 characters) representing an execution capability (e.g., `structured-output`). |
| `AgentCapabilitySet` | Sealed Class | Immutable, sorted, deduplicated capability set with deterministic ordinal ordering and capability queries. |
| `AgentSessionReference` | Sealed Record | Opaque provider-specific conversation or thread identifier preserved verbatim across turns. |
| `ExecutionPermission` | Enum | Declared execution permission level: `ReadOnly`, `WorkspaceWrite`, or `Unrestricted`. |
| `ExecutionEnvironment` | Sealed Record | Environment configuration for execution, encapsulating a non-blank, fully qualified working directory. |
| `StructuredOutputContract` | Sealed Record | Specification requiring syntactically valid JSON Schema text for schema-constrained output. |

---

## IAgentExecutor

The primary entry point for agent execution is defined as:

```csharp
namespace AiRepoKit.Agents;

public interface IAgentExecutor
{
    AgentProviderId ProviderId { get; }

    AgentCapabilitySet Capabilities { get; }

    Task<AgentExecutionResult> ExecuteAsync(
        AgentExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}
```

Key semantics:

- **Provider Identity**: The `ProviderId` property synchronously identifies the underlying provider (`antigravity` or `codex`).
- **Static Capabilities**: The `Capabilities` property synchronously reports the static capabilities supported by the executor without runtime discovery or probing.
- **Single-Request Execution**: `ExecuteAsync` accepts exactly one `AgentExecutionRequest` and returns a completed `AgentExecutionResult`.
- **Caller Cancellation**: Supports cancellation via standard `CancellationToken`. Cancellation terminates the underlying process tree and propagates `OperationCanceledException`.
- **Scope Restriction**: `IAgentExecutor` does not manage multi-agent topologies, workflow DAGs, automatic retries, or execution loops.

---

## Provider Identity and Capabilities

Every provider adapter exposes a fixed identity and static capabilities:

- **Antigravity**:
  - `ProviderId`: `antigravity`
  - `Capabilities`: `structured-output`
- **Codex**:
  - `ProviderId`: `codex`
  - `Capabilities`: `structured-output`

Capabilities are synchronous, immutable metadata fixed at compile time. In V4:
- No provider probing or network discovery occurs.
- No dynamic feature negotiation takes place.

---

## ExecutionPermission

`ExecutionPermission` specifies the permission level granted to the agent for execution:

```csharp
public enum ExecutionPermission
{
    ReadOnly = 1,
    WorkspaceWrite = 2,
    Unrestricted = 3
}
```

The integer enum values represent identifiers, not mathematical or ranking semantics.

### Antigravity Mapping

Antigravity CLI (`agy`) does not support an explicit read-only sandbox mode in headless execution. Therefore:

- `ReadOnly` → Deterministic `Blocked` result without process execution. Returns `AgentExecutionStatus.Blocked` with the diagnostic:
  `"Current Antigravity headless execution cannot represent the requested read-only policy."`
  *(Antigravity plan mode is not substituted for `ReadOnly`.)*
- `WorkspaceWrite` → Maps to CLI argument `--sandbox`.
- `Unrestricted` → Maps to CLI argument `--dangerously-skip-permissions`.

### Codex Mapping

Codex CLI (`codex exec`) supports granular sandbox modes:

- `ReadOnly` → Maps to CLI arguments `--sandbox read-only`.
- `WorkspaceWrite` → Maps to CLI arguments `--sandbox workspace-write`.
- `Unrestricted` → Maps to CLI argument `--dangerously-bypass-approvals-and-sandbox`.

> [!NOTE]
> These command-line flags are adapter-internal implementation choices and are never part of the public domain contracts.

---

## ExecutionEnvironment

The `ExecutionEnvironment` defines the working context where the agent process runs:

```csharp
public sealed record ExecutionEnvironment
{
    public string WorkingDirectory { get; }
    public ExecutionEnvironment(string workingDirectory_) { ... }
}
```

Current V4 rules:
- Encapsulates `WorkingDirectory` only.
- The path must be non-null, non-whitespace, and fully qualified per `Path.IsPathFullyQualified`.
- The supplied path string is retained exactly without alteration.
- Constructor validation does not probe the file system or check directory existence.
- No other runtime environment variables or settings are included in V4.

---

## AgentSessionReference

An `AgentSessionReference` represents an opaque identifier for conversation or thread continuity:

```csharp
public sealed record AgentSessionReference
{
    public string Value { get; }
    public AgentSessionReference(string value_) { ... }
}
```

Rules:
- Treated as an opaque string value.
- Exact pass-through: no trimming, no normalization, no parsing, and no UUID structure validation.

### Provider Behavior

- **Antigravity**:
  - Request: passed to the CLI as `--conversation <exact value>`.
  - Response: parsed from the envelope property `conversation_id` into an `AgentSessionReference`.
- **Codex**:
  - Request: passed as subcommand arguments `exec ... resume <exact value> <instruction>`.
  - Response: parsed from the JSON Lines event `thread.started` (property `thread_id`) into an `AgentSessionReference`.

Session references provide correlation across individual invocations. Full session lifecycle management (e.g., session creation, deletion, persistence, and expiration policies) is deferred to V5.

---

## StructuredOutputContract

The `StructuredOutputContract` enforces schema-conforming structured JSON output:

```csharp
public sealed record StructuredOutputContract
{
    public string JsonSchema { get; }
    public StructuredOutputContract(string jsonSchema_) { ... }
}
```

Rules:
- The constructor verifies that `jsonSchema_` is non-empty and syntactically valid JSON using `JsonDocument.Parse`.
- The exact schema text is retained.
- V4 performs syntax validation only; semantic JSON Schema draft validation is not performed by AI.RepoKit contracts.

### Provider Integration

- **Antigravity**:
  - Passed directly to the CLI as `--json-schema <exact schema>`.
- **Codex**:
  - Written to a private, temporary UTF-8 file without BOM in `%TEMP%` named `codex-schema-<guid>.json`.
  - Passed to the CLI as `--output-schema <temp-file-path>`.
  - The temporary file is deleted in a `finally` block immediately after process execution completes.
  - Temporary file management is private to the Codex adapter and is not exposed publicly.

---

## Result Statuses

`AgentExecutionStatus` defines four terminal statuses:

```csharp
public enum AgentExecutionStatus
{
    Completed = 1,
    Blocked = 2,
    NeedsInput = 3,
    Failed = 4
}
```

### Antigravity Result Mapping

Antigravity outputs a single JSON envelope on stdout with a root `status` field:

| Antigravity Envelope Status | Exit Code | Result Status | Notes |
| --- | --- | --- | --- |
| `SUCCESS` | `0` | `Completed` | Output text mapped from `response`. Stderr attached to diagnostics if present. |
| `SUCCESS` | Non-zero | `Failed` | Failure diagnostic captures non-zero exit code alongside response/stderr. |
| `WAITING` | Any | `NeedsInput` | Indicates interactive prompt or confirmation requirement. |
| `ERROR` | Any | `Failed` | Mapped directly to failure with diagnostic text. |
| `INVALID` | Any | `Failed` | Input validation rejection mapped to failure. |
| `CANCELED` | Any | `Failed` | Mapped to failure unless caller cancellation initiated the abort. |
| `INTERRUPTED` | Any | `Failed` | Mapped to failure unless caller cancellation initiated the abort. |
| `RUNNING` | Any | `Failed` | Non-terminal status in a one-shot `ExecuteAsync` context is treated as a failure. |
| *Unrecognized status* | Any | `Failed` | Unknown status values fail safely. |
| *Malformed / non-JSON* | Any | `Failed` | Syntax errors or non-object roots result in failure. |
| *Empty stdout* | Any | `Failed` | Process produced no output. |

### Codex Result Mapping

Codex outputs a stream of JSON Lines (JSONL) events to stdout. Relevant events:

- `thread.started`: Captures `thread_id` as the session reference.
- `item.completed` (with `item.type == "agent_message"`): Captures message content (`item.text`) as the latest candidate output text.
- `turn.completed`: Indicates successful completion of an execution turn.
- `turn.failed`: Indicates turn-level execution failure with error details.
- `error`: Indicates a top-level error event with message.

Criteria for `Completed`:
1. The process exit code is `0`.
2. At least one `turn.completed` event is observed.
3. No `turn.failed` event is observed.
4. No top-level `error` event is observed.
5. No malformed non-empty JSON lines are encountered.

If all criteria are met, the result is `Completed` with `OutputText` set to the latest agent message text.

Other behaviors:
- Non-zero exit code with `turn.completed` results in `Failed`.
- Absence of `turn.completed` results in `Failed`.
- Syntactically valid unknown event types are safely ignored for forward compatibility.
- Codex does not synthesize `NeedsInput` in V4.

---

## Caller Cancellation

Both adapters implement deterministic cancellation handling:

- **Pre-Execution Cancellation**: If the caller's `CancellationToken` is already canceled before execution begins (`ThrowIfCancellationRequested`), an `OperationCanceledException` is thrown immediately without spawning a process.
- **In-Flight Cancellation**: If cancellation is triggered while the process is active:
  1. The adapter terminates the root process and all child processes (`Kill(entireProcessTree: true)`).
  2. The adapter awaits root process exit using a non-canceled wait (`process.WaitForExitAsync(CancellationToken.None)`).
  3. The adapter rethrows `OperationCanceledException`.
- **Cancellation Integrity**: Caller cancellation is never trapped and converted into an `AgentExecutionStatus.Failed` result.
- **No Implicit Timeouts**: V4 does not configure implicit timeouts. Any execution timeout must be controlled by the caller via the passed `CancellationToken`.

---

## Deterministic Validation

To guarantee stability, safety, and reproducible builds, adapter validation is strictly deterministic:

- **Process Seams**: Tests interact with adapters via internal process runner interfaces (`IAntigravityProcessRunner`, `ICodexProcessRunner`).
- **Offline Execution**: Tests do not invoke vendor CLI executables (`agy`, `codex`).
- **No Credentials**: No API keys, tokens, or credentials are required or checked.
- **No Model / Network Calls**: No live requests are made to AI model endpoints during test runs.

### Local Verification

Run the Release build and automated tests:

```powershell
dotnet build AI.RepoKit.MCP.sln -c Release
dotnet test AI.RepoKit.MCP.sln -c Release --no-build
```

### Baseline Evidence

At the conclusion of V4.P06, the solution test suite passes deterministically:

- Total passed: **1,496 tests**
- Total failed: **0 tests**

*(Note: The count of 1,496 tests is baseline evidence of solution health and not a permanent contract.)*

---

## Platform Matrix

The deterministic test gate runs across the standard GitHub Actions matrix:

| OS Family | CI Representative | V4 Deterministic Gate |
| --- | --- | --- |
| Windows | `windows-2025` | Full solution build + tests (`AI.RepoKit.MCP.sln`) |
| Linux | `ubuntu-24.04` | Full solution build + tests (`AI.RepoKit.MCP.sln`) |

- **Matrix Scope**: macOS is not part of the P06 deterministic matrix.
- **Packaging Scope**: Creating or building release packages for another OS architecture (e.g., Linux ARM64) does not imply separate deterministic certification unless verified by the automated matrix.

---

## Live-Provider Boundary

Deterministic V4 acceptance does not require or perform live provider execution:

- Live provider authentication is neither documented nor required for repository tests.
- Live provider CLI commands are not invoked in repository build or CI pipelines.
- An authorized live smoke test, if conducted manually or separately, is strictly observational and does not serve as the authoritative deterministic CI gate.

---

## V5 Deferred Responsibilities

To maintain a minimal, robust architecture in V4, the following responsibilities are explicitly deferred to V5 and later roadmap phases:

- Reusable process and model execution runtimes
- Microsoft.Extensions.AI adapter wrapping
- Built-in timeout policies and enforcement
- Real-time token and response streaming
- Provider and model discovery or health checks
- Dynamic model selection and routing
- Runtime retry and backoff policies
- Provider fallback logic
- Agent session lifecycle and state management (creation, eviction, persistence)
- Workflow orchestration, DAG execution, and workflow resume
