# AI.RepoKit V5 Model Runtime

This document provides the human-facing technical reference guide for the Model Execution Runtime in AI.RepoKit V5.

The V5 model execution runtime is implemented in the repository under `src/AiRepoKit.Agents.Runtime`. **v5.0.0 is unreleased**; v3.0.0 remains the released, current baseline until a separate release publication decision is made. V5 builds directly upon the provider-neutral execution abstractions established in V4 ([`v4-agent-execution.md`](v4-agent-execution.md)).

---

## Scope

AI.RepoKit V5 introduces a reusable process execution runtime and a modular, provider-neutral model execution subsystem:

- **Reusable Process Execution Runtime**: General-purpose cross-platform process runner abstractions and system process implementation with timeout coordination, process-tree termination, and standard stream capture.
- **Provider-Neutral Model Execution Contracts**: Core contracts defining model execution requests, responses, telemetry, and execution lifecycles in `AiRepoKit.Agents.Runtime`.
- **Microsoft.Extensions.AI Adapter Boundary**: Encapsulation of Microsoft.Extensions.AI `IChatClient` beneath AI.RepoKit-owned runtime contracts without delegating orchestration or resilience policies to third-party libraries.
- **Timeout and Caller Cancellation**: Explicit request timeout coordination enforced per candidate attempt alongside terminal, non-retried caller cancellation handling.
- **Stateless Streaming Execution**: Real-time response update streaming with an explicit stream exposure boundary governing failure recovery.
- **Session Lifecycle Management**: Conversational session creation, single-turn concurrency enforcement, explicit session termination, and ChatClient-backed conversation-state commit after successful completion.
- **Provider and Model Registration and Discovery**: Registrations pairing provider identity, model identity, static capability sets, and optional asynchronous health probes.
- **Deterministic Capability and Health Routing**: Multi-tier routing ordering candidates strictly by capability compatibility, health status, provider identity, and model identity.
- **Token Usage, Pricing, and Telemetry**: Normalized token counters, caller-configured token pricing models, latency measurement, and successful-execution telemetry.
- **Runtime Resilience, Retry, and Fallback**: Pluggable failure classification, fixed same-candidate retry backoff, ordered stateless candidate fallback, and sticky session resume.
- **Deterministic Platform Acceptance**: Automated verification across Windows (`windows-2025`) and Linux (`ubuntu-24.04`) in CI without requiring authenticated live provider/model execution or credentials.

> [!IMPORTANT]
> **Model execution is not workflow orchestration.**
> V5 focuses strictly on executing model requests, managing streaming responses, and coordinating single-session conversational continuity. V5 does not implement workflow checkpoints, DAG execution, multi-agent collaboration, job continuation, or ExecutableWork recovery. Workflow orchestration remains distinct roadmap work.

> [!IMPORTANT]
> **Runtime retry is distinct from semantic repair.**
> The V5 resilience policy operates strictly on candidate execution exceptions, with retryability determined exclusively by the caller-supplied `IModelExecutionFailureClassifier`. Successful response content is never inspected to trigger runtime retry or fallback. V5 does not implement response-quality repair, schema or business validation repair loops, prompt rewriting, or replanning. Structured output schema configuration via the adapter boundary is distinct from semantic repair.

---

## Architecture Ownership and Boundaries

The V5 model runtime subsystem establishes clear boundaries between contracts, concrete implementations, and third-party libraries:

```text
AiRepoKit.Agents.Abstractions (V4 domain types)
             ^
             |
AiRepoKit.Agents.Runtime (V5 reusable runtime engine)
             |
             +--> Microsoft.Extensions.AI.Abstractions 10.10.0 (adapter boundary only)
```

Key architectural principles:

- **Abstractions Independence**: `AiRepoKit.Agents.Abstractions` remains zero-dependency and does not reference `Microsoft.Extensions.AI`, `AiRepoKit.Spec`, or `AiRepoKit.Cli`.
- **V4 Abstractions Reuse**: `AiRepoKit.Agents.Runtime` directly consumes V4 domain types including `AgentCapabilitySet`, `AgentSessionReference`, `StructuredOutputContract`, and `AgentProviderId`.
- **AI.RepoKit Ownership**: AI.RepoKit owns model execution contracts, routing algorithms, resilience logic, session coordination, and telemetry models. Ownership is never delegated to external frameworks.
- **Encapsulated Adapter Boundary**: `ChatClientModelExecutionRuntime` provides the explicit adapter boundary over `Microsoft.Extensions.AI.Abstractions 10.10.0`. Its public constructors intentionally accept `IChatClient`, allowing callers to construct the adapter with an `IChatClient` instance. Model execution, streaming, session lifecycle, routing, and resilience semantics are exposed and governed through AI.RepoKit-owned runtime contracts; P05 routing and P07 retry/fallback are not delegated to Microsoft.Extensions.AI.
- **No Standalone Package Distribution**: `AiRepoKit.Agents.Runtime` is configured with `<IsPackable>false</IsPackable>` and is not published as a standalone NuGet package.
- **No CLI Materialization in V5**: `AiRepoKit.Cli` does not reference `AiRepoKit.Agents.Runtime`. V5 model runtime capabilities are not exposed through an `airepo` command.

---

## Public Contract Surface

The public contract surface in `AiRepoKit.Agents.Runtime` consists of exactly 26 authorized types:

| Type | Kind | Purpose |
| --- | --- | --- |
| `IProcessExecutionRuntime` | Interface | Core contract for general-purpose asynchronous process execution. |
| `SystemProcessExecutionRuntime` | Sealed Class | Concrete cross-platform process runner managing process lifetimes and streams. |
| `ProcessExecutionRequest` | Sealed Record | Immutable specification for process execution (executable, arguments, working directory, timeout). |
| `ProcessExecutionResult` | Sealed Record | Immutable outcome of process execution (exit code, standard output, standard error). |
| `IModelExecutionRuntime` | Interface | Core contract for single-turn model request execution. |
| `ModelExecutionRequest` | Sealed Record | Immutable input specification for model execution (prompt, structured output, per-attempt timeout). |
| `ModelExecutionResult` | Sealed Record | Immutable model execution outcome (response text, optional telemetry). |
| `ChatClientModelExecutionRuntime` | Sealed Class | Adapter executing model requests via Microsoft.Extensions.AI `IChatClient`. |
| `IModelSessionRuntime` | Interface | Contract for stateful model execution within an explicit session lifecycle. |
| `IResumableModelSessionRuntime` | Interface | Semantic marker interface combining streaming and session runtime capabilities. |
| `IModelStreamingExecutionRuntime` | Interface | Contract for streaming model response deltas in stateless or session contexts. |
| `ModelExecutionUpdate` | Sealed Record | Incremental streaming update containing a response-text delta and optional telemetry. |
| `ModelRuntimeRegistration` | Sealed Class | Registration record pairing provider identity, model identity, capabilities, runtime, and health probe. |
| `IModelDiscoveryRuntime` | Interface | Contract for discovering model registrations and evaluating health snapshots. |
| `ConfiguredModelDiscoveryRuntime` | Sealed Class | In-memory discovery implementation enforcing identity uniqueness and health probe execution. |
| `ModelHealthSnapshot` | Sealed Record | Immutable health snapshot pairing provider/model identity with health status. |
| `ModelHealthStatus` | Enum | Health status values: `Unknown`, `Healthy`, `Unhealthy`. |
| `IModelRoutingRuntime` | Interface | Contract for routing requests to candidate models based on required capabilities. |
| `DeterministicModelRoutingRuntime` | Sealed Class | Deterministic capability and health routing engine producing ordered candidate lists. |
| `ModelRouteCandidate` | Sealed Record | Route entry pairing a registration with its observed health status. |
| `ModelTokenUsage` | Sealed Record | Normalized token usage counters (input, output, total, cached input, reasoning). |
| `ModelTokenPricing` | Sealed Record | Caller-supplied pricing configuration per million tokens with currency code validation. |
| `ModelExecutionTelemetry` | Sealed Record | Telemetry for successful executions (token usage, latency, estimated cost, currency). |
| `ModelRuntimeResiliencePolicy` | Sealed Record | Configuration for maximum attempts per candidate and fixed retry backoff. |
| `IModelExecutionFailureClassifier` | Interface | Pluggable classifier evaluating candidates and exceptions for retry eligibility. |
| `ResilientModelExecutionRuntime` | Sealed Class | Resilient execution coordinator implementing retry, fallback, and sticky session resume. |

---

## Process Execution Runtime

The process execution subsystem provides a reusable, cross-platform foundation for spawning system processes:

```csharp
namespace AiRepoKit.Agents.Runtime;

public interface IProcessExecutionRuntime
{
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}
```

### ProcessExecutionRequest and ProcessExecutionResult

- **`ProcessExecutionRequest`**:
  - `Executable`: Non-null, non-whitespace path or executable name.
  - `Arguments`: Read-only list of string arguments copied into an immutable collection.
  - `WorkingDirectory`: Non-null, non-whitespace path validated to be fully qualified via `Path.IsPathFullyQualified`.
  - `Timeout`: Optional `TimeSpan` validated to be strictly positive and no greater than 4,294,967,294 milliseconds (~49.7 days).
- **`ProcessExecutionResult`**:
  - `ExitCode`: Integer exit code returned by the terminated process.
  - `StandardOutput`: Standard output string decoded as UTF-8.
  - `StandardError`: Standard error string decoded as UTF-8.

### SystemProcessExecutionRuntime

The concrete `SystemProcessExecutionRuntime` executes system processes using standard .NET process APIs:

- Launches processes with `UseShellExecute = false`, `CreateNoWindow = true`, and UTF-8 stream encodings.
- Does not invoke intermediate shells (`pwsh`, `cmd`, `sh`, `bash`).
- On caller cancellation: terminates the entire process tree (`Kill(entireProcessTree: true)`), awaits process exit without cancellation, and rethrows `OperationCanceledException`.
- On timeout expiry: terminates the entire process tree, awaits process exit, and throws a `TimeoutException`.
- Supports dependency-injected `TimeProvider` for deterministic, virtual-time testing.

---

## Model Execution Request and Result

Model execution centers on `IModelExecutionRuntime`:

```csharp
namespace AiRepoKit.Agents.Runtime;

public interface IModelExecutionRuntime
{
    Task<ModelExecutionResult> ExecuteAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}
```

### ModelExecutionRequest

`ModelExecutionRequest` encapsulates input parameters:

- `Prompt`: Non-null, non-whitespace text prompt.
- `StructuredOutput`: Optional `StructuredOutputContract` requiring syntactically valid JSON Schema text.
- `Timeout`: Optional per-attempt execution timeout (strictly positive, up to 4,294,967,294 milliseconds).

> [!IMPORTANT]
> **Timeout scope is strictly per-attempt.**
> `REQUEST_TIMEOUT_SCOPE=PER_ATTEMPT`
> `ModelExecutionRequest.Timeout` governs a single candidate execution attempt. AI.RepoKit does not enforce a global resilience timeout across multiple retries or fallback candidates.
> `ModelExecutionRequest` does not specify model IDs, provider IDs, retry policies, cost thresholds, or routing preferences.

### ModelExecutionResult

`ModelExecutionResult` represents the outcome of execution:

- `ResponseText`: Completed model response text (non-null).
- `Telemetry`: Optional `ModelExecutionTelemetry` capturing token counts, elapsed latency, and estimated cost.

---

## Caller Cancellation Semantics

Caller cancellation is authoritative and non-negotiable throughout the runtime:

- **Precedence**: `CALLER_CANCELLATION_PRECEDENCE=HIGHEST`.
- **Pre-Execution Check**: If `cancellationToken.IsCancellationRequested` is true prior to execution, `OperationCanceledException` is thrown immediately before routing, process invocation, or attempt dispatch.
- **In-Flight Cancellation**: The caller token is passed to the active runtime. Once caller cancellation is observed by AI.RepoKit, cancellation is terminal for resilience processing: no failure classification, retry, or fallback is performed. Whether an injected runtime can immediately terminate its own underlying external operation is implementation-specific (for example, `SystemProcessExecutionRuntime` terminates the process tree, and `ChatClientModelExecutionRuntime` coordinates cancellation with `IChatClient`, but immediate external termination is not guaranteed for arbitrary injected runtimes).
- **Resilience Invariant**:
  - `OPERATION_CANCELED_EXCEPTION_RETRY=NO`
  - `OPERATION_CANCELED_EXCEPTION_FALLBACK=NO`
- `OperationCanceledException` is never classified as retryable and never triggers candidate fallback. It is immediately terminal.

---

## Stateless Streaming Execution

Streaming execution is defined by `IModelStreamingExecutionRuntime`:

```csharp
namespace AiRepoKit.Agents.Runtime;

public interface IModelStreamingExecutionRuntime : IModelSessionRuntime
{
    IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingAsync(
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);

    IAsyncEnumerable<ModelExecutionUpdate> ExecuteStreamingInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);
}
```

### ModelExecutionUpdate

Streaming emits `ModelExecutionUpdate` records:

- `ResponseTextDelta`: Non-null text delta for the current chunk.
- `Telemetry`: Optional `ModelExecutionTelemetry`.

`Telemetry` is optional at the contract level, allowing arbitrary `IModelStreamingExecutionRuntime` implementations to emit telemetry updates according to their contract. `ResilientModelExecutionRuntime` forwards exact candidate updates unchanged without manufacturing an additional synthetic telemetry update. `ChatClientModelExecutionRuntime` specifically emits its successful accumulated telemetry in one synthetic final update after natural stream completion and successful disposal. Any telemetry-bearing update counts as an exposed update that closes automatic retry and fallback.

### Stream Exposure Boundary

`ResilientModelExecutionRuntime` enforces an explicit stream exposure boundary to ensure output integrity:

- Updates are exposed to the caller as they arrive from the underlying provider.
- Output is never buffered in memory to manufacture replay safety.
- **Before First Yield**: If an exception occurs before the first update is yielded to the caller, the runtime may retry the candidate or fall back to an alternate candidate if authorized by the resilience policy and failure classifier.
- **After First Yield**: Once any update has been yielded to the caller (including empty deltas, content deltas, or telemetry-bearing updates), **all automatic retry and fallback are permanently closed**. Any subsequent failure propagates directly to the caller.

### Streaming Disposal and Error Boundary

When disposing streaming enumerators under failure conditions, `ResilientModelExecutionRuntime` guarantees clean exception semantics:

- **Primary Failure Precedence**: If streaming fails with a primary exception (cancellation, network error, or contract failure) and the underlying enumerator disposal also throws, the secondary disposal exception is suppressed so the primary exception propagates unaltered. No `AggregateException` is manufactured.
- **Disposal-Only Failure**: If stream enumeration completes normally but enumerator disposal fails, the disposal exception propagates.
- **Token Advancement Guard**: Caller cancellation is re-checked immediately before and after advancing enumerators (`MoveNextAsync`) so that a misbehaving dependency ignoring cancellation cannot yield subsequent updates once cancellation has occurred.

---

## Agent and Model Session Lifecycle

Conversational multi-turn continuity is managed through `IModelSessionRuntime` and `IResumableModelSessionRuntime`:

```csharp
namespace AiRepoKit.Agents.Runtime;

public interface IModelSessionRuntime : IModelExecutionRuntime
{
    AgentSessionReference CreateSession();

    Task<ModelExecutionResult> ExecuteInSessionAsync(
        AgentSessionReference sessionReference_,
        ModelExecutionRequest request_,
        CancellationToken cancellationToken_ = default);

    Task<bool> EndSessionAsync(
        AgentSessionReference sessionReference_,
        CancellationToken cancellationToken_ = default);
}

public interface IResumableModelSessionRuntime : IModelStreamingExecutionRuntime
{
}
```

### Session Creation and Lazy Binding

- `CreateSession()` returns a new, unique opaque `AgentSessionReference`. Wrapper session identifiers in `ResilientModelExecutionRuntime` are non-blank and unique; they do not encode provider or model identity, and callers must not parse or depend on a GUID or UUID format.
- In `ResilientModelExecutionRuntime`, `CreateSession()` does not route or contact providers. The wrapper session is initially unbound.
- **Lazy Binding**: Upon the first turn execution (`ExecuteInSessionAsync` or `ExecuteStreamingInSessionAsync`), the runtime acquires a route and binds to the first candidate implementing `IResumableModelSessionRuntime`.
- **Sticky Binding**: Once bound, the wrapper session remains permanently bound to the same candidate runtime and underlying session. **No cross-candidate session migration is supported.**

### Session Concurrency

- `SESSION_CONCURRENT_TURNS=NO`.
- Exactly one active execution turn is permitted per session at any time.
- If a turn is initiated while another turn is active on the same session reference, the runtime throws `InvalidOperationException` immediately. Turns are never silently queued.
- Initial lazy binding is synchronized within the same turn gate.

### Successful-State Commit Semantics

`IModelSessionRuntime` defines lifecycle operations but does not mandate how arbitrary implementations persist or mutate their own conversational state. `ResilientModelExecutionRuntime` owns wrapper binding, retry coordination, and session lifecycle behavior, but delegates candidate session execution and does not own candidate conversation history. Resilient wrapper sessions remain bound and reusable after an execution failure.

In `ChatClientModelExecutionRuntime`, in-memory ChatClient-backed conversation state (history and conversation identifiers) is managed with explicit successful-commit semantics:
- Conversation state is committed into session memory only after a turn completes successfully.
- For streaming sessions, state commits only after natural end-of-stream and clean enumerator disposal.
- Failed, canceled, timed-out, or incomplete turns do not commit new conversation state to session history.
- The session reference remains valid and may be reused for subsequent attempts unless explicitly ended.

Arbitrary caller-injected `IModelSessionRuntime` implementations define their own internal state persistence strategies.

### EndSession Semantics

- **Unknown or Already Ended**: Calling `EndSessionAsync` with an unknown or previously ended session returns `false`.
- **Unbound Session**: If the session was never used for execution, it is marked ended and removed immediately, returning `true` without routing or provider calls.
- **Bound Session**: Awaits any active turn, delegates termination to the bound candidate's `EndSessionAsync`, removes the wrapper session if the call completes normally, and returns the underlying boolean result.
- **Failure Resilience**: If the underlying `EndSessionAsync` call throws an exception or caller cancellation occurs, the wrapper session is retained in memory, allowing subsequent explicit termination attempts. No automatic retry or fallback is performed for `EndSessionAsync`.

---

## Model Discovery and Health

The discovery subsystem defines static registrations and dynamic health monitoring:

```csharp
namespace AiRepoKit.Agents.Runtime;

public interface IModelDiscoveryRuntime
{
    IReadOnlyList<ModelRuntimeRegistration> Discover();

    Task<IReadOnlyList<ModelHealthSnapshot>> CheckHealthAsync(
        CancellationToken cancellationToken_ = default);
}
```

### ModelRuntimeRegistration

`ModelRuntimeRegistration` defines an available model candidate:

- `ProviderId`: `AgentProviderId` (e.g., `openai`, `anthropic`, `local`).
- `ModelId`: Non-null, non-whitespace model identifier string.
- `Capabilities`: Immutable `AgentCapabilitySet` declaring supported capabilities.
- `Runtime`: Underlying `IModelExecutionRuntime` instance.
- `HealthProbe`: Optional asynchronous probe delegate: `Func<CancellationToken, ValueTask<ModelHealthStatus>>`.

### Health Status and Snapshots

`ModelHealthStatus` represents the health condition of a model:

```csharp
public enum ModelHealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Unhealthy = 2
}
```

- **`ModelHealthSnapshot`**: Immutable record pairing `ProviderId`, `ModelId`, and `ModelHealthStatus`.
- In `ConfiguredModelDiscoveryRuntime`:
  - Registrations missing a health probe automatically report `ModelHealthStatus.Unknown`.
  - When health probes throw an exception (other than caller cancellation), the status defaults safely to `ModelHealthStatus.Unhealthy`.
  - Registrations are validated for identity uniqueness and sorted ordinally by `ProviderId` then `ModelId`.

---

## Deterministic Model Routing

Candidate selection is governed by `IModelRoutingRuntime`:

```csharp
namespace AiRepoKit.Agents.Runtime;

public interface IModelRoutingRuntime
{
    Task<IReadOnlyList<ModelRouteCandidate>> RouteAsync(
        AgentCapabilitySet requiredCapabilities_,
        CancellationToken cancellationToken_ = default);
}
```

### DeterministicModelRoutingRuntime Algorithm

`DeterministicModelRoutingRuntime` determines candidate ordering using strict deterministic rules:

1. **Discovery**: Queries `IModelDiscoveryRuntime.Discover()`.
2. **Capability Filtering**: Discards registrations for which `registration.Capabilities.SupportsAll(requiredCapabilities_)` returns false. If none qualify, returns an empty route.
3. **Health Evaluation**: Queries `IModelDiscoveryRuntime.CheckHealthAsync()`, verifying exact one-to-one correspondence with discovered models.
4. **Unhealthy Exclusion**: Excludes all candidates with `ModelHealthStatus.Unhealthy`.
5. **Health Tiering**: Groups candidates into tiers where `Healthy` (tier 0) precedes `Unknown` (tier 1).
6. **Ordinal Tie-Breaking**:
   - Within each tier, candidates are sorted by `ProviderId.Value` using ordinal comparison (`string.CompareOrdinal`).
   - If provider IDs match, candidates are sorted by `ModelId` using ordinal comparison (`string.CompareOrdinal`).

```text
Discovered Registrations
          |
          v
Capability Match? --------[No]---> Discard
          |
         [Yes]
          |
          v
Health == Unhealthy? -----[Yes]--> Discard
          |
         [No]
          |
          v
Order: Healthy (0) -> Unknown (1)
          |
          v
Tie-break: ProviderId (Ordinal) -> ModelId (Ordinal)
```

> [!NOTE]
> `P05_ROUTING_POLICY_MUTATION=NO`
> `TELEMETRY_ROUTING=NO`
> Routing never performs dynamic scoring, latency weighting, pricing optimization, benchmark evaluation, or provider favoritism.

### Routing Capability Boundary

Routing evaluates requirements strictly against an `AgentCapabilitySet`. The planned `ModelRequirement` concept is not implemented in V5 and remains deferred to **V6.P04 (issue #44)**.

---

## Token Usage, Pricing, and Telemetry

The telemetry subsystem normalizes token consumption, caller pricing, and execution metrics:

### ModelTokenUsage

`ModelTokenUsage` normalizes token counts across providers:

- `InputTokenCount`: Nullable non-negative integer (`long?`).
- `OutputTokenCount`: Nullable non-negative integer (`long?`).
- `TotalTokenCount`: Nullable non-negative integer (`long?`).
- `CachedInputTokenCount`: Nullable non-negative integer (`long?`), cannot exceed `InputTokenCount`.
- `ReasoningTokenCount`: Nullable non-negative integer (`long?`), cannot exceed `OutputTokenCount`.

> [!NOTE]
> `TotalTokenCount = InputTokenCount + OutputTokenCount` is **not** an invariant. The runtime validates each supplied count independently according to the `ModelTokenUsage` contract and does not enforce that arithmetic relationship.

### ModelTokenPricing

`ModelTokenPricing` defines caller-supplied pricing configuration:

- `CurrencyCode`: Exactly 3 uppercase ASCII letters (e.g., `USD`, `EUR`).
- `InputCostPerMillionTokens`: Non-negative decimal rate per million input tokens.
- `OutputCostPerMillionTokens`: Non-negative decimal rate per million output tokens.
- `CachedInputCostPerMillionTokens`: Optional non-negative decimal rate per million cached input tokens.

Pricing values are caller configuration. AI.RepoKit does not discover or infer provider/model market prices, bundle provider pricing catalogs, or perform remote pricing lookups. When an explicit cached-input rate is omitted (`null`), estimated-cost calculation uses the configured input-token rate (`InputCostPerMillionTokens`) for cached input; the stored property itself remains `null`.

### ModelExecutionTelemetry and Cost Calculation

`ModelExecutionTelemetry` captures execution metrics:

- `TokenUsage`: Optional normalized `ModelTokenUsage`.
- `Latency`: Non-negative `TimeSpan` representing elapsed execution duration.
- `EstimatedCost`: Optional non-negative decimal estimated cost.
- `CostCurrencyCode`: Matching 3-letter currency code (required when `EstimatedCost` is specified, null otherwise).

Estimated cost is calculated using exact decimal arithmetic when pricing is supplied and both input and output token counts are present:

1. Let $\text{CachedTokens} = \text{CachedInputTokenCount}$ if specified, otherwise $0$.
2. Let $\text{NonCachedInput} = \text{InputTokenCount} - \text{CachedTokens}$.
3. Let $\text{CachedRate} = \text{CachedInputCostPerMillionTokens}$ if specified, otherwise $\text{InputCostPerMillionTokens}$.
4. Let $\text{InputCost} = (\text{NonCachedInput} \times \text{InputCostPerMillionTokens}) + (\text{CachedTokens} \times \text{CachedRate})$.
5. Let $\text{OutputCost} = \text{OutputTokenCount} \times \text{OutputCostPerMillionTokens}$.
6. $\text{EstimatedCost} = (\text{InputCost} + \text{OutputCost}) / 1{,}000{,}000$.

### Telemetry Boundary

- Telemetry represents successful execution outcomes.
- For streaming via `ChatClientModelExecutionRuntime`, the final synthetic update contains the accumulated telemetry.
- Telemetry from failed attempts is not aggregated or exposed across retries (`TELEMETRY_AGGREGATION_ACROSS_ATTEMPTS=NO`, `FAILED_ATTEMPT_TELEMETRY_PUBLIC_SURFACE=NO`).
- Telemetry records never contain prompt text, response text, raw vendor payloads, timestamps, or error details.

---

## Runtime Resilience: Retry, Fallback, and Session Resume

Resilience orchestration is encapsulated in `ResilientModelExecutionRuntime`:

```csharp
namespace AiRepoKit.Agents.Runtime;

public sealed record ModelRuntimeResiliencePolicy
{
    public int MaxAttemptsPerCandidate { get; }
    public TimeSpan RetryBackoff { get; }

    public ModelRuntimeResiliencePolicy(int maxAttemptsPerCandidate_, TimeSpan retryBackoff_) { ... }
}

public interface IModelExecutionFailureClassifier
{
    bool IsRetryable(ModelRouteCandidate candidate_, Exception exception_);
}
```

### Policy Rules

- `MaxAttemptsPerCandidate`: Total attempts allowed per candidate (must be $\ge 1$; includes the initial attempt).
- `RetryBackoff`: Fixed delay between retries on the same candidate (must be $\ge 0$, up to 4,294,967,294 milliseconds).
- Backoff is fixed: no exponential multiplier, no jitter, and no randomization.
- No delay occurs before the initial attempt, and no delay occurs when transitioning from an exhausted candidate to the next fallback candidate.

### Failure Classification

- `IModelExecutionFailureClassifier` is pluggable. AI.RepoKit provides no default retry classifier; the calling application defines the classification strategy.
- The classifier receives the exact `ModelRouteCandidate` and candidate `Exception`.
- A failure is retried only if the classifier explicitly returns `true`.
- Non-retryable exceptions are immediately terminal.

### Stateless Retry and Fallback Lifecycle

For stateless execution (`ExecuteAsync` and `ExecuteStreamingAsync` prior to the first yielded update):

1. The runtime acquires the candidate route from `IModelRoutingRuntime`.
2. For each candidate in route order:
   - Executes attempt 1.
   - On exception: checks caller cancellation (terminal). Evaluates classifier.
   - If retryable and attempts remain: delays by `RetryBackoff` and retries the same candidate.
   - If attempts on the current candidate are exhausted: advances to the next candidate in the route without delay (fallback).
3. If all candidates are exhausted, the terminal exception from the final candidate propagates directly.
4. The runtime never manufactures an `AggregateException` or custom resilience exception wrapper.

### Session Resume Lifecycle

For stateful session execution (`ExecuteInSessionAsync` and `ExecuteStreamingInSessionAsync` prior to the first yielded update):

- The session lazily binds to the first candidate implementing `IResumableModelSessionRuntime`.
- Retries remain strictly on the bound candidate and underlying session reference.
- **No candidate fallback**: If the bound candidate exhausts its attempts, the exception propagates. The session does not migrate to an alternate candidate.
- `ResilientModelExecutionRuntime` keeps the wrapper bound to the same runtime and session reference after failed turns. When the bound candidate is `ChatClientModelExecutionRuntime`, its conversation state is committed only on successful completion as described above.

---

## Architectural Boundaries

### Runtime Retry vs. Semantic Repair

`RUNTIME_RETRY_DISTINCT_FROM_SEMANTIC_REPAIR=YES`
`SEMANTIC_REPAIR=NO`

The V5 resilience subsystem operates strictly on candidate execution exceptions, with retryability decided by the caller-supplied `IModelExecutionFailureClassifier`. It does not:
- Inspect response text or evaluate completion quality.
- Detect malformed JSON or validate schema/business rules.
- Reprompt models, adjust sampling parameters, or rewrite prompts.
- Execute validation repair or replanning loops.

Structured output schema handling through the ChatClient adapter remains distinct from runtime resilience.

### Agent Session Resume vs. Workflow Resume

`AGENT_SESSION_RESUME_DISTINCT_FROM_WORKFLOW_RESUME=YES`
`WORKFLOW_RESUME=NO`

Session resumption maintains conversational thread continuity with a specific model backend. It does not:
- Checkpoint agent workflow graphs or DAG states.
- Persist step execution state across process boundaries.
- Resume interrupted multi-step workflows.

### Privacy and Data Boundary

- `ModelExecutionRequest` prompts and structured output schemas are intentionally provided to the selected runtime adapter for execution.
- V5 Runtime does not add a new public persistence, export, or audit-logging contract for prompts or responses.
- `ModelExecutionTelemetry` does not capture prompt text, response text, or model identifiers.
- Caller-injected `IChatClient` implementations, health probes, and surrounding application code define their own external behavior; AI.RepoKit does not guarantee that injected dependencies never persist, log, or transmit supplied data.
- Deterministic acceptance requires no authenticated live provider/model execution and requires no live provider credentials. Caller-injected dependencies or health probes may perform external network activity according to their own implementations. Normal build and package restore infrastructure is not declared network-free.

### Provider Neutrality

The core runtime contains no built-in provider/model-specific routing or resilience special cases:
- The core routing engine has no built-in provider or model preference table; routing remains deterministic based on capability matching, health status, `ProviderId`, and `ModelId`.
- The core resilience engine has no built-in provider-specific failure or retry table; failure classification is governed by caller-supplied `IModelExecutionFailureClassifier` implementations.
- No model benchmark rankings, latency estimates, or recommended model lists are bundled in AI.RepoKit.
- Caller-provided registrations, health probes, `IChatClient` implementations, and failure classifiers may naturally implement distinct, provider-specific behavior. The failure classifier intentionally receives the exact `ModelRouteCandidate` and exception to support caller-defined strategies.

### CLI and Runtime Materialization Boundary

`AiRepoKit.Cli` does not reference `AiRepoKit.Agents.Runtime` and does not provide an `airepo` command for model execution in V5. CLI tests exercise solution coexistence, not runtime CLI integration.

---

## Platform Matrix and Acceptance

Deterministic acceptance tests run across the standard GitHub Actions matrix:

| OS Family | CI Representative | V5 Deterministic CI Gate |
| --- | --- | --- |
| Windows | `windows-2025` | Full solution build + tests (`AI.RepoKit.MCP.sln`) + CLI smoke (`--version`, `doctor`, `plan`) |
| Linux | `ubuntu-24.04` | Full solution build + tests (`AI.RepoKit.MCP.sln`) |

- **Matrix Scope**: macOS is not part of the deterministic acceptance matrix.
- **Packaging Scope**: Building or packaging target executables does not constitute deterministic platform certification.

---

## Validation Commands

To build and verify the V5 model runtime and full solution locally:

```powershell
# Build the model runtime project with warnings as errors
dotnet build src/AiRepoKit.Agents.Runtime/AiRepoKit.Agents.Runtime.csproj -c Release -warnaserror

# Execute runtime test suite
dotnet test tests/AiRepoKit.Agents.Runtime.Tests/AiRepoKit.Agents.Runtime.Tests.csproj -c Release

# Build entire solution with warnings as errors
dotnet build AI.RepoKit.MCP.sln -c Release -warnaserror

# Execute full solution automated test suite
dotnet test AI.RepoKit.MCP.sln -c Release --no-build
```

### Baseline Evidence

At the conclusion of V5.P08 acceptance, test suites pass deterministically:

- `AiRepoKit.Agents.Runtime.Tests`: **543 passed**, 0 failed, 0 skipped.
- Full solution (`AI.RepoKit.MCP.sln`): **2,033 passed**, 0 failed, 0 skipped.

*(Note: Test counts reflect P08 acceptance evidence and are not permanent product invariants across future roadmap changes.)*

---

## Release Boundary

**v5.0.0 is unreleased.**

The completion of V5.P08 documents and validates the accepted runtime implementation. P08 does not bump package versions, create Git tags, or publish a GitHub Release. Release publication remains a separate decision.
