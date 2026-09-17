# Changelog

All notable changes to AI.RepoKit are documented in this file.

The project follows Semantic Versioning.

## [Unreleased]

### Agent Execution (V4)

#### Added

- Provider-neutral agent execution contracts in `AiRepoKit.Agents.Abstractions`:
  - `IAgentExecutor`: provider-neutral interface exposing synchronous `ProviderId` and static `Capabilities` metadata with single-request `ExecuteAsync` execution and caller cancellation support.
  - Public contract types: `AgentExecutionRequest`, `AgentExecutionResult`, `AgentExecutionStatus`, `AgentProviderId`, `AgentCapability`, `AgentCapabilitySet`, `AgentSessionReference`, `ExecutionPermission`, `ExecutionEnvironment`, and `StructuredOutputContract`.
  - Deterministic capability metadata via `AgentCapabilitySet` with synchronous querying and validation (advertising `structured-output` for both adapters without runtime discovery or probing).
  - Explicit execution permission contracts (`ReadOnly`, `WorkspaceWrite`, `Unrestricted`) as distinct identifiers without numeric ranking assumptions.
  - Fully qualified, non-probing working directory abstraction via `ExecutionEnvironment`.
  - Syntactically validated JSON schema specification via `StructuredOutputContract`.
  - Opaque provider session-reference pass-through via `AgentSessionReference` without string trimming, normalization, parsing, or UUID validation.
  - Four terminal execution statuses in `AgentExecutionStatus`: `Completed`, `Blocked`, `NeedsInput`, and `Failed`.
- Concrete Antigravity CLI adapter in `AiRepoKit.Agents.Antigravity` (`AntigravityCliClientAdapter`):
  - Maps `WorkspaceWrite` to `--sandbox` and `Unrestricted` to `--dangerously-skip-permissions`.
  - Enforces deterministic `Blocked` status without process execution for `ReadOnly` permissions.
  - Supports structured output schema via `--json-schema` and session continuation via `--conversation`.
  - Maps Antigravity JSON envelope outputs (`SUCCESS`, `WAITING`, `ERROR`, `INVALID`, `CANCELED`, `INTERRUPTED`, `RUNNING`) to domain `AgentExecutionStatus`.
- Concrete Codex CLI adapter in `AiRepoKit.Agents.Codex` (`CodexCliClientAdapter`):
  - Maps `ReadOnly` to `--sandbox read-only`, `WorkspaceWrite` to `--sandbox workspace-write`, and `Unrestricted` to `--dangerously-bypass-approvals-and-sandbox`.
  - Supports structured output via a private temporary UTF-8-without-BOM schema file passed to `--output-schema` and cleaned up in a `finally` block.
  - Supports session resumption via `resume <session-id>` command syntax.
  - Parses streaming Codex JSON Lines (JSONL) events (`thread.started`, `item.completed`, `turn.completed`, `turn.failed`, top-level `error`) into domain results and captures the latest agent message text.
- Process-tree cancellation behavior across both adapters:
  - Pre-canceled tokens propagate `OperationCanceledException` before process launch.
  - In-flight caller cancellation triggers entire process-tree termination followed by an un-canceled wait for root exit, propagating `OperationCanceledException` without converting to `Failed`.
- Full-solution Windows and Linux CI validation in `.github/workflows/ci.yml` and `.github/workflows/release.yml`:
  - Upgraded test step to execute `dotnet test AI.RepoKit.MCP.sln -c Release --no-build` across `windows-2025` and `ubuntu-24.04`.
- Comprehensive V4 agent execution documentation guide in `docs/v4-agent-execution.md`.

#### Architectural Boundaries & V5 Deferrals

- Zero external `PackageReference` dependencies in `AiRepoKit.Agents.Abstractions` and zero references to `AiRepoKit.Spec`.
- No `Microsoft.Extensions.AI` or Agent Framework abstractions entered V4 domain contracts.
- Provider CLI flags and temporary schema file handling remain private adapter implementation details, never domain contracts.
- Reusable process and model runtime, timeout policies, streaming, provider/model discovery, health probes, model selection, dynamic routing, retry policies, fallback orchestration, and session lifecycle management are explicitly deferred to V5.

## [3.0.0] - 2026-09-14

### Spec-Driven Development

AI.RepoKit v3.0.0 introduces formal Spec-Driven Development (SDD), providing typed,
versioned Intermediate Representation (IR) artifacts, cryptographic approval ledgers,
deterministic semantic diffing, evidence-backed verification, and read-only MCP Spec
context integration.

#### Added

- Formal Spec IR schema v1 (`https://ai.repokit.dev/schemas/spec/v1/spec-ir.schema.json`)
  defining typed contracts for `RequirementInput`, `Requirement`, `RequirementSet`,
  `Constraint`, `AcceptanceCriterion`, `WorkSpec`, `PlanStep`, `ImplementationPlan`,
  `Approval`, `VerificationEvidence`, and `VerificationResult`.
- Canonical Spec workspace persistence under `.ai/specs/<spec-id>/` for requirements,
  work specifications, implementation plans, and approval ledgers.
- Directory-level concurrency coordination (`SpecWorkspaceWriteCoordinator`) and atomic
  payload writes (`SpecAtomicFileWriter`) protecting canonical Spec files from partial
  writes and symlink traversal.
- Bounded `SpecContext` builder and persistence service producing derived, budget-aware
  projections under `.ai/generated/spec-context/<spec-id>.json`.
- Cryptographically bound approval ledger (`approvals.json`) recording approvals with
  SHA-256 semantic digests (`SpecSemanticDigest`), revision numbers, and approver identity.
- Approval status evaluation engine reporting `Current`, `Stale`, and `NotApproved` states
  with automatic invalidation when upstream dependencies are modified.
- Implementation checklist projector (`ImplementationChecklistProjector`) generating
  dynamic in-memory Markdown and JSON checklists from canonical plans without persisting
  derived checklist state.
- Semantic diff analyzer (`SpecDiffAnalyzer`) comparing candidate IR against canonical
  state and projecting exact property differences and downstream invalidation impacts.
- Evidence-backed verification adapter, prerequisite validator, and evaluator supporting
  strict outcomes (`PASS`, `FAIL`, `NOT_VERIFIED`) where missing evidence is never `PASS`
  and unsupported LLM assertions are rejected.
- Read-only Spec context integration in the Portable MCP runtime (`airepo mcp serve`)
  exposing `spec`, `spec-context`, and `verification` kinds through `get_context`.
- New `airepo spec` CLI command group providing `init`, `show`, `refine`, `plan`,
  `approve`, `checklist`, `diff`, and `verify` subcommands with dry-run default behavior.
- Comprehensive Spec-Driven Development documentation guide (`docs/v3-spec-driven-development.md`).

#### Changed

- CLI package and tool version bumped to 3.0.0 across metadata, build, and runtime version reporting.
- Portable MCP capabilities in `get_health` advertise `spec`, `spec-context`, and `verification`
  while preserving the fixed compact surface: 5 tools, 9 resources, and 17 prompts.
- Top-level `airepo plan` remains distinct and backwards-compatible with v1/v2 repository
  setup, while `airepo spec plan` governs canonical v3 implementation plans.

#### Compatibility & Migration

- Additive and opt-in: Upgrading to v3.0.0 does not create `.ai/specs/` or alter existing
  repository configurations.
- Existing v2 repositories continue running all existing workflows (`audit`, `plan`, `setup`,
  `update`, `self-check`, `mcp-diagnose`, hooks, context-packs) without modification.
- Spec IR schema v1 is immutable; workspaces produced during v3 development remain fully valid.
- Mutating Spec commands default to dry-run previews; canonical changes require explicit `--apply`.
- Portable MCP server remains session-repository-bound, read-only, and bounded by context budgets.

## [2.0.0] - 2026-08-24

### Cross-platform runtime

AI.RepoKit v2.0.0 completes the cross-platform runtime migration for Windows,
Ubuntu, and WSL, with product business logic no longer depending on PowerShell.

#### Added

- Cross-platform script-shell selection and runtime execution infrastructure.
- Native executable resolution and script-runner abstractions.
- Native MCP response-budget service with deterministic protocol, security,
  report, and integration tests.
- Native SDK-alignment service.
- Native AI-context update service that generates the MCP context manifest,
  project inventory, project references, package inventory, SDK inventory,
  and generated context summary without requiring PowerShell.
- Native secret-scan service preserving the historical filtering and report
  contract with deterministic cross-platform execution and redacted findings.
- Dedicated native build-diagnostics service and CLI entrypoint preserving the
  historical report, process, output-tail, and exit-code contracts.
- Dedicated .NET SDK probe runner that preserves the historical successful
  `dotnet --version` and `dotnet --list-sdks` artifact contract while keeping
  diagnostic errors redacted.
- Portable MCP runtime hosted by the AI.RepoKit CLI through `airepo mcp serve`
  and `airepo mcp serve --repo <path>`, without requiring an MCP DLL inside
  the target repository.
- Deterministic SDK-alignment tests covering project discovery, target
  frameworks, ignored paths, report generation, and process failures.
- Deterministic AI-context update tests covering generated artifacts, project
  discovery, references, packages, SDK discovery, runtime options, ignored
  paths, timestamps, failure atomicity, and output ordering.

#### Changed

- Generated MCP client configurations now launch the portable `airepo mcp serve`
  runtime instead of a target-repository `AiRepo.ContextMcp.dll`.
- MCP diagnostics, SelfCheck, Bootstrap, and MCP budget execution are portable-first;
  legacy repo-local MCP build/runtime state is compatibility information rather
  than a prerequisite for portable operation.
- SelfCheck, Efficiency, Bootstrap, and MCP diagnostics use the native MCP
  budget service instead of PowerShell product business logic.
- Bootstrap runs AI-context update and SDK alignment natively.
- Bootstrap runs secret scanning natively and no longer executes
  `CheckSecrets.ps1` for product runtime behavior.
- RoslynLite code indexing is authoritative in Bootstrap; native code-index
  failures are explicit errors and no longer fall back to
  `UpdateCodeInventory.ps1`.
- SelfCheck no longer requires the secret-scan PowerShell compatibility script
  to exist at runtime.
- SelfCheck no longer requires the build-diagnostics PowerShell compatibility
  script to exist at runtime.
- SelfCheck no longer requires the code-inventory PowerShell compatibility script
  to exist at runtime.
- Native build diagnostics explicitly preserves the historical Windows
  PowerShell `*.sln` filesystem wildcard behavior, including matching root
  `.slnx` files, consistently across platforms.
- Dedicated native CLI entrypoints now expose AI-context update, SDK alignment,
  secret scan, and MCP response-budget functionality for compatibility wrappers.
- Historical AI-context PowerShell helpers are thin wrappers over native
  `airepo` commands instead of containing product business logic.
- Equivalent Bash compatibility wrappers are generated and managed alongside
  the PowerShell wrappers.
- Security-review context recommendations use native `airepo secret-scan`
  rather than a Windows-only PowerShell invocation.
- Stale MCP host discovery no longer launches Windows PowerShell; Windows
  process discovery uses the native .NET `System.Management` WMI API over
  `Win32_Process`, while process termination remains handled by `System.Diagnostics`.
- `--stop-stale-mcp-hosts` remains explicitly Windows-only; Linux and WSL
  return the existing unsupported result without attempting process discovery.
- `UpdateAiContext.ps1` and `CheckSdkAlignment.ps1` are no longer executed by
  Bootstrap for product runtime behavior.
- SelfCheck no longer requires the migrated UpdateAiContext or SDK-alignment
  PowerShell compatibility scripts to exist.
- SDK-alignment report generation is cross-platform on Windows, Linux, and WSL.
- SDK-alignment project paths and output ordering are deterministic.
- Native SDK inventory and SDK-alignment reports preserve the successful raw
  `dotnet --list-sdks` output required for compatibility with the historical
  PowerShell-generated artifacts.

#### Compatibility

- Existing repo-local MCP launch remains supported as an explicit legacy
  compatibility path during migration; portable runtime failures do not silently
  fall back to the legacy runtime.
- Historical PowerShell helpers remain available as thin compatibility wrappers
  over native AI.RepoKit CLI entrypoints.
- Bash equivalents are available for all six historical AI-context helpers.
- PowerShell and Bash wrappers are generated from paired templates and tracked
  through the managed-files system.
- `UpdateAiContext.ps1` continues accepting the historical `-Apply` switch while
  delegating to the native AI-context update command.
- `MeasureMcpResponseBudget.ps1` continues accepting the historical
  `-FailOnBudget` switch while preserving the native `0/1/2` exit contract.
- Generated Bash wrappers are marked executable on Unix-like platforms.
- ConfigGenerator manages both PowerShell and Bash compatibility artifacts.

#### Validation

- Native MCP budget migration validated on Windows and WSL.
- Native SDK alignment validated with real `dotnet` execution on Windows and WSL.
- Native AI-context update validated with real execution on Windows and WSL.
- AI-context generated-artifact parity against the legacy PowerShell
  implementation: 6 of 6 artifacts passing on Windows.
- SDK-alignment semantic parity against the legacy PowerShell implementation
  passing on Windows, including the raw SDK-list contract.
- Native secret scan validated with real execution on Windows and WSL.
- Secret-scan semantic parity against the historical PowerShell implementation
  validated on Windows.
- Explicit secret-value non-disclosure validation passed for reports and
  surfaced scanner output.
- Secret-scan service tests: 21 passing after final diagnostic-contract review.
- AI-context update service tests: 18 passing.
- SDK-alignment service tests: 17 passing.
- Bootstrap integration tests: 23 passing.
- SelfCheck tests: 8 passing.
- P02.3 WSL acceptance full test suite: 223 passing.
- P02.3 Windows acceptance full test suite: 223 passing.
- Native build diagnostics validated with real execution on Windows and WSL.
- Build-diagnostics semantic parity against the historical Windows PowerShell
  implementation passed for no-solution, target selection, failure exit-code
  precedence, and bounded diagnostic output tails.
- P02.4 WSL acceptance full test suite: 241 passing.
- P02.4 Windows acceptance full test suite: 241 passing.
- Native RoslynLite code indexing validated with real execution on Windows and WSL.
- Explicit native code-index failure behavior validated on Windows and WSL.
- P02.5 WSL acceptance full test suite: 241 passing.
- P02.5 Windows acceptance full test suite: 241 passing.
- P03 Bash compatibility wrappers validated with real execution on WSL,
  including argument forwarding and the 12-call MCP budget matrix.
- P03 PowerShell compatibility wrappers validated with real Windows PowerShell
  execution, including legacy parameter acceptance and the 12-call MCP budget
  matrix.
- P03 wrapper/template parity, ConfigGenerator integration, managed-files
  tracking, and Unix executable-bit behavior validated.
- P04 stale-MCP-host handling validated with 8 deterministic service tests
  covering platform behavior, candidate matching, path-boundary protection,
  successful termination, termination failure, and current-process protection.
- P04 real Windows acceptance validated native WMI discovery and selective
  termination of the matching MCP `dotnet` host while preserving a sibling
  path-prefix collision process.
- P04 WSL acceptance full test suite: 257 passing.
- P04 Windows acceptance full test suite: 257 passing.
- R01 portable MCP acceptance passed on WSL and Windows with the exact existing
  MCP surface preserved: 5 tools, 9 resources, and 17 prompts.
- R01 real protocol acceptance passed on WSL and Windows with strict JSON-RPC
  stdout, empty stderr, zero target-repository mutations, and no target-repository
  MCP DLL dependency.
- R01 explicit legacy repo-local MCP compatibility passed on WSL and Windows.
- R01 focused test suite: 103 passing on both WSL and Windows.
- R01 closure full test suite: 279 passing on both WSL and Windows.
- P05 Windows Release acceptance full test suite: 280 passing.
- P05 WSL Release acceptance full test suite: 280 passing.
- P05 GitHub CI passed on Windows Server 2025 and Ubuntu 24.04.
- P05 CLI install/update acceptance passed from v1.8.1 to v2.0.0 on Windows; v2.0.0 package installation also passed on WSL.
- P05 Bootstrap, Update, SelfCheck, Efficiency, MCP smoke, and portable MCP runtime acceptance passed on Windows and WSL.
- P05 WSL product acceptance passed with no `pwsh` installed, confirming no unexpected PowerShell product dependency.
- P05 release workflow validation-only dispatch passed without creating a v2.0.0 tag or GitHub Release.
- P05 release candidate artifact contract verified: 7 of 7 files, version 2.0.0 manifest, and all SHA256 hashes matched.
