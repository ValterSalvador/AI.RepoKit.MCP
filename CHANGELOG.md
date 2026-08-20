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

- Historical PowerShell compatibility artifacts are retained until P03.
- `Tools/AiContext/CheckSdkAlignment.ps1` remains available as a compatibility
  artifact but is no longer used for product runtime behavior.
- `Tools/AiContext/UpdateAiContext.ps1` remains available as a compatibility
  artifact but is no longer used for product runtime behavior.
- `Tools/AiContext/CheckSecrets.ps1` remains available as a compatibility
  artifact until P03 but is no longer used by Bootstrap product runtime.
- `Tools/AiContext/InvokeBuildDiagnostics.ps1` remains available unchanged as
  a compatibility artifact until P03; native C# is the runtime source of truth.
- `Tools/AiContext/UpdateCodeInventory.ps1` remains available unchanged as a
  compatibility artifact until P03 and is no longer a Bootstrap runtime fallback.
- ConfigGenerator continues to generate the historical compatibility artifacts.

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

### Release policy

- Do not publish v1.9.0.
- The next cross-platform release is v2.0.0.
- Final release notes will be curated from this Unreleased section after P05.
