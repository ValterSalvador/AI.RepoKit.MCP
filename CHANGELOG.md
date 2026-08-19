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
- Native SDK-alignment service using the existing process-runner abstraction.
- Deterministic SDK-alignment tests covering project discovery, target
  frameworks, ignored paths, report generation, and process failures.

#### Changed

- SelfCheck, Efficiency, Bootstrap, and MCP diagnostics use the native MCP
  budget service instead of PowerShell product business logic.
- Bootstrap now runs SDK alignment natively after UpdateAiContext.
- SDK-alignment report generation is cross-platform on Windows, Linux, and WSL.
- SDK-alignment project paths and output ordering are deterministic.

#### Compatibility

- Historical PowerShell compatibility artifacts are retained until P03.
- `Tools/AiContext/CheckSdkAlignment.ps1` remains available as a compatibility
  artifact but is no longer used for Bootstrap product runtime behavior.
- `UpdateAiContext.ps1` remains runtime-backed by PowerShell and is the next
  P02.2 migration slice.

#### Validation

- Native MCP budget migration validated on Windows and WSL.
- Native SDK alignment validated with real `dotnet` execution on Windows and WSL.
- SDK-alignment service tests: 17 passing.
- Bootstrap integration tests: 19 passing.
- Current WSL full test suite: 179 passing.

### Release policy

- Do not publish v1.9.0.
- The next cross-platform release is v2.0.0.
- Final release notes will be curated from this Unreleased section after P05.
