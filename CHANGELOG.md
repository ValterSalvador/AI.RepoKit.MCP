# Changelog

All notable changes to AI.RepoKit are documented in this file.

The project follows Semantic Versioning.

## [Unreleased]

### v2.0.0 — Cross-platform runtime

AI.RepoKit v2.0.0 is under active development. The release will not be
published until the Windows, Ubuntu, and WSL acceptance gates are complete.

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
- Deterministic SDK-alignment tests covering project discovery, target
  frameworks, ignored paths, report generation, and process failures.
- Deterministic AI-context update tests covering generated artifacts, project
  discovery, references, packages, SDK discovery, runtime options, ignored
  paths, timestamps, failure atomicity, and output ordering.

#### Changed

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

### Release policy

- Do not publish v1.9.0.
- The next cross-platform release is v2.0.0.
- Final release notes will be curated from this Unreleased section after P05.
