# AI.RepoKit.MCP

Generic .NET local tool for planning, validating, and bootstrapping AI context and MCP infrastructure in target .NET repositories.

Status: v1.0 stable release hardening; terminal progress UX; MCP/client diagnostics; profile-driven VS Code agent, instruction, and prompt generation; self-check; managed agent instructions; audit baseline; RoslynLite code-index cache; and task-oriented context packs for lower-token local LLM context.

## Goals

- Provide a .NET tool named `airepo`.
- Provide self-contained single-file executables for Windows and Linux.
- Default to dry-run behavior.
- Write only when `--apply` is explicit.
- Avoid reading or copying secrets and generated/runtime folders.
- Keep generated inventories and reports under `.ai/generated/`.
- Keep all behavior parameterized by the target repository.
- Start with Codex, GitHub Copilot for VS Code, and GitHub Copilot for Visual Studio.
- Plan optional snippets for Claude Desktop, Cursor, and Gemini without overwriting global configuration.

## Quick Install And Use

Use a standalone release executable when you want the least setup. Download the GitHub Release asset for your platform, extract it, and run from a target repository:

```powershell
.\airepo.exe --version
.\airepo.exe audit --repo .
.\airepo.exe plan --repo . --clients codex,vscode,vs --mcp --agents --profile dotnet
```

Use a local tool when you want repository-pinned installation:

```powershell
dotnet new tool-manifest
dotnet tool install AiRepoKit.Cli --add-source artifacts/nuget
dotnet tool run airepo --version
dotnet tool run airepo -- plan --repo . --clients codex,vscode,vs --mcp --agents --profile dotnet
```

Use a global tool when you want `airepo` on your PATH:

```powershell
dotnet tool install --global AiRepoKit.Cli --add-source artifacts/nuget
airepo --version
airepo plan --repo . --clients codex,vscode,vs --mcp --agents --profile dotnet
```

GitHub Releases are the distribution channel. NuGet.org publishing is not enabled; the `.nupkg` is attached to GitHub Releases for local source installation.

## Recommended Safe First Run

Start with read-only and generated-output checks before writing repository files:

```powershell
airepo audit --repo .
airepo plan --repo . --clients codex,vscode,vs --mcp --agents --profile dotnet
airepo code-index --repo . --apply
airepo context-pack --repo . --task review-risk --apply
airepo bootstrap --repo . --clients codex,vscode,vs --mcp --agents --profile dotnet --apply --backup
airepo self-check --repo . --agents --profile dotnet --skip-build-mcp
airepo mcp-diagnose --repo . --clients codex,vscode,vs --skip-build
git status --short
```

Use `--profile demo` for a broad demonstration profile that combines common .NET, web, migration, security, and desktop guidance without targeting a specific internal project. `vs` is the preferred Visual Studio client name; `visualstudio` remains accepted only as a legacy alias.

Long-running CLI commands show terminal progress when running interactively. Progress and spinner output is written to stderr, never stdout. `--json` disables progress automatically so JSON stdout stays parseable. Use `--no-progress` to disable progress output, and `--verbose` to keep existing detailed reports plus additional phase detail where supported.

## Real Repository Flow

Create and review the audit baseline before committing generated guidance:

```powershell
airepo audit --repo .
airepo audit --repo . --create-baseline
```

Manually review `.ai/policies/audit-baseline.json`. Keep new entries as `review-required` until a person decides whether each finding should remain blocking, be marked `accepted`, or be marked `false-positive`.

Then bootstrap with the selected profile and commit only versionable files:

```powershell
airepo bootstrap --repo . --clients codex,vscode,vs --mcp --agents --profile dotnet --apply --backup
airepo self-check --repo . --agents --profile dotnet
airepo mcp-diagnose --repo . --clients codex,vscode,vs
git status --short
```

Versionable files include `.ai/` guidance, `.ai/policies/audit-baseline.json`, `Tools/AiContext/`, `Tools/AiContextMcp/`, `.vscode/mcp.json`, `.github/` agent and instruction files, and `AGENTS.md`. Local generated outputs under `.ai/generated/`, local Codex config, build output, release artifacts, and copied standalone executables stay ignored.

## VS Code Agent Flow

Open VS Code at the repository root so `${workspaceFolder}` in `.vscode/mcp.json` resolves correctly. If the MCP server is not visible after bootstrap or diagnostics pass, run `Developer: Reload Window` or close and reopen the workspace.

In Copilot Agent or another MCP-capable assistant, start with the `ai_repo_context` server before opening many source files:

```text
get_repo_brief
get_health area=all
get_context kind=context-packs detail=brief
search_context query="<task keywords>" limit=10
```

Use context packs and indexed summaries before opening files. Request a specific pack when the brief output identifies one that matches the task. Fall back to source files only when MCP context is insufficient.

Natural-language activation shortcuts are documentation guidance for agents, not a CLI feature. Prefix a request with `ai-repo:` to ask the assistant to use `ai_repo_context` first:

```text
ai-repo: explain how bootstrap validates MCP diagnostics
ai-repo plan: add focused tests for context pack selection
ai-repo review: check this API change for compatibility risk
```

Supported optional sub-prefixes are `ai-repo ask:`, `ai-repo plan:`, `ai-repo fix:`, `ai-repo review:`, and `ai-repo test:`. Agents should start with compact calls to `get_repo_brief`, `get_health`, `get_context kind=context-packs detail=brief`, and `search_context` with the user task, keep responses token-efficient, avoid broad file reads, and avoid write, build, server, Docker, migration, SQL, or database commands unless the selected role and user permission allow them. v1.0.0 does not include a CLI prompt translator and does not add MCP tools for these shortcuts.

## Release And Versioning

Tag releases with full SemVer tags:

```powershell
git tag vX.Y.Z
git push origin vX.Y.Z
```

GitHub Actions strips the leading `v` and uses the tag version for the NuGet package, standalone executables, archives, and `release-manifest.json`. GitHub Releases only are enabled. NuGet.org publishing is intentionally not enabled.

For a local release validation build:

```powershell
dotnet build -c Debug
powershell -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -Version 1.0.0
artifacts/publish/win-x64/airepo.exe --help
artifacts/publish/win-x64/airepo.exe self-check --repo . --agents --profile dotnet --skip-build-mcp
artifacts/publish/win-x64/airepo.exe mcp-diagnose --repo . --clients codex,vscode,vs --skip-build
artifacts/publish/win-x64/airepo.exe audit --repo .
git status --short
```

## v1.0 Readiness Checklist

- CLI help covers bootstrap, init, plan, code-index, context-pack, audit, self-check, mcp-diagnose, doctor, validate, and sample.
- Examples use `--clients codex,vscode,vs`; `visualstudio` is documented only as a legacy alias.
- Recommended flows include audit, plan, code-index, context-pack, bootstrap, self-check, and mcp-diagnose.
- Audit baseline behavior is documented, with manual review required before accepting findings.
- Profile guidance is explicit, especially `--profile dotnet` and `--profile demo`.
- MCP diagnostics pass without changing MCP tools or protocol.
- Generated outputs under `.ai/generated/` are ignored and reproducible.
- Release artifacts include standalone executables, local `.nupkg`, wrapper files, and `artifacts/release-manifest.json`.
- Releases are created from `vX.Y.Z` tags through GitHub Releases only.
- NuGet.org publishing remains disabled.

## Install As Local Tool

```powershell
dotnet new tool-manifest
dotnet pack src/AiRepoKit.Cli/AiRepoKit.Cli.csproj -c Release -o artifacts/nuget
dotnet tool install AiRepoKit.Cli --add-source artifacts/nuget
dotnet tool run airepo --version
dotnet tool run airepo doctor
```

To update an existing local tool installation:

```powershell
dotnet tool update AiRepoKit.Cli --add-source artifacts/nuget
```

## Install As Global Tool

```powershell
dotnet pack src/AiRepoKit.Cli/AiRepoKit.Cli.csproj -c Release -o artifacts/nuget
dotnet tool install --global AiRepoKit.Cli --add-source artifacts/nuget
airepo --version
```

To update an existing global tool installation:

```powershell
dotnet tool update --global AiRepoKit.Cli --add-source artifacts/nuget
```

For local development without installing:

```powershell
dotnet run --project src/AiRepoKit.Cli/AiRepoKit.Cli.csproj -- --help
```

## Generate Release Artifacts

Windows:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-Release.ps1
```

By default, the release script uses the current `<Version>` from `src/AiRepoKit.Cli/AiRepoKit.Cli.csproj`.

To override the version explicitly:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Build-Release.ps1 -Version 1.0.0
```

Linux:

```bash
bash scripts/build-release.sh
```

The release scripts run `airepo audit` before packaging unless `-SkipAudit` or `--skip-audit` is provided. The audit blocks on high-severity local path, explicit personal or sandbox development names, and likely secret findings.

The release scripts generate:

```text
artifacts/nuget/AiRepoKit.Cli.<version>.nupkg
artifacts/publish/win-x64/airepo.exe
artifacts/publish/win-x64/install-ai-context.cmd
artifacts/publish/win-x64/install-ai-context.ps1
artifacts/publish/linux-x64/airepo
artifacts/publish/linux-arm64/airepo
artifacts/release-manifest.json
```

Windows and Linux use different binaries. Use `airepo.exe` on Windows and `airepo` on Ubuntu/Linux. The standalone binaries are self-contained, so the target machine does not need the .NET runtime installed.

## CI/Release

GitHub Actions runs CI on `push` and `pull_request` for Windows and Ubuntu. CI restores, builds in Release, packs the local tool package, uploads the generated `.nupkg`, and runs basic Windows smoke checks for `--version`, `doctor`, and `plan`.

Releases are created by pushing a full SemVer tag that matches `v*.*.*`, for example:

```powershell
git tag vX.Y.Z
git push origin vX.Y.Z
```

The release workflow removes the leading `v` from the tag and uses that version for the NuGet package, standalone artifacts, and release manifest. Local release scripts use the `.csproj` version by default, but GitHub Actions SemVer tags continue to use the tag version. A `vX.Y.Z` tag produces:

```text
AiRepoKit.Cli.X.Y.Z.nupkg
airepo-win-x64.zip
airepo-linux-x64.tar.gz
airepo-linux-arm64.tar.gz
release-manifest.json
```

GitHub Releases are internal distribution only. NuGet.org publishing is not enabled. The `.nupkg` is attached to the GitHub Release for download or local source installation only.

To use the Windows executable from a GitHub Release, open the release for the tag, download `airepo-win-x64.zip`, extract it, and run:

```powershell
.\airepo.exe --version
.\airepo.exe doctor
```

## Use Standalone Executables

Windows:

```powershell
artifacts/publish/win-x64/airepo.exe --version
artifacts/publish/win-x64/airepo.exe doctor
artifacts/publish/win-x64/airepo.exe bootstrap --clients codex,vscode,vs --mcp
artifacts/publish/win-x64/airepo.exe bootstrap --clients codex,vscode,vs --mcp --agents --apply --backup
artifacts/publish/win-x64/airepo.exe self-check
artifacts/publish/win-x64/airepo.exe mcp-diagnose --clients codex,vscode,vs
```

The primary standalone flow on Windows uses only `airepo.exe`:

```text
1. Copy airepo.exe to the repository root.
2. Double click airepo.exe.
3. Choose option 4, bootstrap apply with backup.
4. Type APPLY when prompted (case-insensitive).
```

If `airepo.exe` is opened directly with a double click and no arguments, it starts a safe interactive mode. The interactive mode detects a repository candidate from the executable folder first, falls back to the current directory when needed, and shows whether it found a `.sln` or `.slnx`, a `.csproj`, `.ai`, and `Tools/AiContextMcp`. It also shows the selected/default profile and explains that progress is shown while longer operations run. The menu offers `doctor`, `plan`, `bootstrap dry-run`, `bootstrap apply with backup`, `validate`, `explain what will be installed`, `explain profiles`, and `exit`.

Before interactive apply, the CLI shows that it will create repository-local `.ai/`, `Tools/AiContext/`, `Tools/AiContextMcp/`, `.codex/config.toml`, `.vscode/mcp.json`, and generic profile `.github` agent/instruction/prompt files, build the MCP project in Release, run AI context scripts, and show progress while work is running. It uses safe defaults: `--clients codex,vscode,vs --mcp --agents --profile generic --apply --backup`. It also states that it will not run the target server, Docker, migrations, SQL, or database commands, and will not read or copy secrets. The apply option requires typing `APPLY` in a case-insensitive way before any write is attempted.

To use the standalone executable from a terminal:

```powershell
.\airepo.exe doctor --repo .
.\airepo.exe plan --repo . --clients codex,vscode,vs --mcp
.\airepo.exe bootstrap --repo . --clients codex,vscode,vs --mcp
.\airepo.exe bootstrap --repo . --clients codex,vscode,vs --mcp --apply --backup
.\airepo.exe bootstrap --repo . --clients codex,vscode,vs --mcp --agents --apply --backup
.\airepo.exe self-check --repo .
.\airepo.exe mcp-diagnose --repo . --clients codex,vscode,vs
.\airepo.exe doctor
.\airepo.exe bootstrap --clients codex,vscode,vs --mcp --apply --backup
.\airepo.exe code-index --apply
```

The CLI still defaults to dry-run whenever `--apply` is not provided.

Progress output is optional. Use `--no-progress` to suppress spinner and phase messages. Use `--verbose` for more report detail and extra phase detail where a command can provide it. Progress uses stderr only, and commands that emit JSON with `--json` disable progress automatically.

`vs` is the preferred client name for GitHub Copilot for Visual Studio. The older `visualstudio` client name remains accepted as a deprecated compatibility alias and writes the same `.ai/client-configs/visualstudio-mcp.snippet.json` snippet.

For commands that accept `--repo`, the option is optional. If omitted, `airepo` first tries the executable folder and then the current directory. A folder is treated as a target repository when it has a `.sln`, `.slnx`, root or immediate `.csproj`, `.git`, `.ai`, or `Tools/AiContextMcp`. If neither location looks valid, pass `--repo <path>`.

### Optional Wrappers

The release also includes optional Windows wrappers. They are convenience files, not required for the main standalone flow. Copy these files into the repository root only if you want a one-click wrapper:

```text
airepo.exe
install-ai-context.cmd
install-ai-context.ps1
```

Then double click `install-ai-context.cmd`. The wrapper changes to its own folder and runs:

```powershell
.\airepo.exe bootstrap --repo . --clients codex,vscode,vs --mcp --apply --backup
```

Ubuntu/Linux x64:

```bash
chmod +x artifacts/publish/linux-x64/airepo
artifacts/publish/linux-x64/airepo --version
artifacts/publish/linux-x64/airepo doctor --repo .
```

Ubuntu/Linux ARM64:

```bash
chmod +x artifacts/publish/linux-arm64/airepo
artifacts/publish/linux-arm64/airepo --version
artifacts/publish/linux-arm64/airepo doctor --repo .
```

## Commands

```powershell
airepo sample --repo <path>
airepo sample --repo <path> --apply
airepo sample --repo <path> --apply --force
airepo plan --repo <path>
airepo init --repo <path> --clients codex,vscode,vs --mcp
airepo init --repo <path> --clients codex,vscode,vs --mcp --agents
airepo init --repo <path> --clients codex,vscode,vs --mcp --apply --backup
airepo bootstrap --repo <path> --clients codex,vscode,vs --mcp
airepo bootstrap --repo <path> --clients codex,vscode,vs --mcp --agents
airepo bootstrap --repo <path> --clients codex,vscode,vs --mcp --apply --backup
airepo validate --repo <path>
airepo configs --repo <path> --clients codex,vscode,vs,claude,cursor,gemini
airepo doctor --repo <path>
airepo code-index --repo <path>
airepo code-index --repo <path> --apply
airepo code-index --repo <path> --apply --rebuild-cache
airepo code-index --repo <path> --apply --no-cache
airepo context-pack --repo <path>
airepo context-pack --repo <path> --task change-api --target User --apply
airepo audit --repo <path>
airepo audit --repo <path> --json
airepo self-check --repo <path>
airepo self-check --repo <path> --agents
airepo --help
airepo --version
```

Equivalent standalone examples when `airepo.exe` is copied to the repository root:

```powershell
.\airepo.exe doctor
.\airepo.exe bootstrap --clients codex,vscode,vs --mcp --agents --apply --backup
.\airepo.exe code-index --apply
.\airepo.exe self-check
```

## Sample Sandbox

Dry-run:

```powershell
airepo sample --repo .tmp/SampleRepo
```

Create the sandbox:

```powershell
airepo sample --repo .tmp/SampleRepo --apply
```

`sample` creates a minimal .NET solution and class library:

```text
SampleRepo.sln
src/Sample.Domain/Sample.Domain.csproj
```

It does not create appsettings, Docker files, migrations, SQL, database commands, or sensitive configuration.

## Bootstrap

Dry-run in a sandbox:

```powershell
airepo bootstrap --repo .tmp/SampleRepo --clients codex,vscode,vs --mcp
```

Apply in a sandbox:

```powershell
airepo bootstrap --repo .tmp/SampleRepo --clients codex,vscode,vs --mcp --apply --backup
```

Apply with optional agent instructions:

```powershell
airepo bootstrap --repo .tmp/SampleRepo --clients codex,vscode,vs --mcp --agents --apply --backup
```

Apply in an existing repository:

```powershell
airepo bootstrap --repo <target> --clients codex,vscode,vs --mcp --apply --backup
```

`bootstrap` runs `doctor`, `plan`, `init`, and `validate`. When `--mcp` and `--apply` are active, it runs the internal RoslynLite code index after init, builds the generated MCP project in Release, and then runs generated AI context scripts when they exist. If RoslynLite fails, bootstrap reports a warning and falls back to `Tools/AiContext/UpdateCodeInventory.ps1` when that script exists. If RoslynLite succeeds, the PowerShell heuristic inventory is not run, so it does not overwrite the RoslynLite output.

Skip options:

```text
--skip-build-mcp
--skip-ai-context
--skip-code-inventory
--skip-security-scan
--skip-budget
--skip-scripts
```

`bootstrap` never runs the target project, Docker, migrations, SQL, or database commands.

## Recommended Release-Hardening Flow

```powershell
airepo audit --repo <target>
airepo plan --repo <target> --clients codex,vscode,vs --mcp --agents --profile dotnet
airepo code-index --repo <target> --apply
airepo context-pack --repo <target> --task review-risk --apply
airepo bootstrap --repo <target> --clients codex,vscode,vs --mcp --agents --profile dotnet --apply --backup
airepo self-check --repo <target> --agents --profile dotnet
airepo mcp-diagnose --repo <target> --clients codex,vscode,vs
git status --short
```

Commit the versionable files after review. Local generated outputs under `.ai/generated/` remain ignored.

No separate release-check command exists in v0.13. Use the v1.0 readiness checklist and the release validation commands above so release hardening stays documentation-only and does not add command surface before v1.0.

## Self Check

`airepo self-check` runs a focused reliability check for an initialized repository. It resolves the repository like other commands, checks repository detection and required generated files, runs audit unless skipped, refreshes the RoslynLite code index unless skipped, verifies `.ai/generated/cache/code-index-cache.json` after code-index runs, builds the generated MCP project in Release unless skipped, runs the MCP response budget script unless skipped, verifies `.ai/generated`, verifies the managed-files manifest after bootstrap, and verifies the AiRepoKit `.gitignore` section. If code-index is skipped, the cache check is skipped too.

```powershell
airepo self-check --repo .
airepo self-check --repo . --agents
airepo self-check --repo . --json
airepo self-check --repo . --context-packs
airepo self-check --repo . --skip-build-mcp --skip-code-index --skip-budget --skip-audit
```

Exit code `0` means no required checks failed, `2` means at least one required check failed, and `1` means a fatal self-check error occurred. `--agents` makes the optional generated agent and instruction files required for the check. If `.ai/generated/context-packs/` exists, self-check validates readable JSON files. `--context-packs` makes at least one context pack required.

## MCP Diagnostics

`airepo mcp-diagnose` checks whether the repository MCP project and selected client configuration are generated, built, and smoke-testable. It does not run the target application, Docker, migrations, SQL, or database commands, and it does not add MCP tools or change the MCP protocol.

```powershell
airepo mcp-diagnose --repo . --clients codex,vscode,vs
airepo mcp-diagnose --repo . --clients vscode --skip-build
airepo mcp-diagnose --repo . --skip-build --skip-smoke --skip-budget
airepo mcp-diagnose --repo . --json --verbose
```

Default clients are `codex,vscode,vs`. `visualstudio` remains accepted as a legacy alias for `vs`.

The command validates repository root, `Tools/AiContextMcp`, the generated Release DLL, selected client config files, `dotnet`, Release MCP build, a lightweight JSON-RPC initialize/tools-list smoke test, and `Tools/AiContext/MeasureMcpResponseBudget.ps1`. The smoke test verifies `get_repo_brief`, `get_health`, `get_policy`, `get_context`, and `search_context`.

Use `--skip-build`, `--skip-smoke`, or `--skip-budget` when you need to isolate config checks or avoid rebuilding while an MCP client has the DLL loaded. Exit code `0` means required diagnostics passed, `2` means required diagnostics failed, and `1` means a fatal diagnostics error occurred.

When VS Code config uses `${workspaceFolder}`, open the workspace at the repository root. If `.vscode/mcp.json` is correct and the smoke test passes but `ai_repo_context` is still not visible in VS Code/Copilot Agent, close and reopen the VS Code workspace or run `Developer: Reload Window`. If the MCP DLL was rebuilt, MCP clients may need a restart or reload before they use the new binary.

## Troubleshooting

### MCP build fails because DLL is locked

If bootstrap, self-check, or a manual MCP build fails because `Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll` is being used by another process, close VS Code, Codex, Copilot Agent, or any MCP client using this repository, then retry bootstrap, self-check, or:

```powershell
dotnet build Tools/AiContextMcp/AiRepo.ContextMcp.csproj -c Release
```

Do not delete project files. Stop the running dotnet/MCP process or close the client that is using the MCP server.

If you only want non-build validation while MCP is currently active, rerun self-check with `--skip-build-mcp`. The MCP build check will be reported as skipped instead of failed.

For client-focused diagnostics while the MCP DLL may be locked, use:

```powershell
airepo mcp-diagnose --repo . --skip-build
```

## MCP Bootstrap

`airepo init --mcp` plans or generates a complete generic AI Context + MCP scaffold:

- `.ai/**` context documents, playbooks, budgets, and manifest.
- `Tools/AiContext/*.ps1` diagnostic scripts.
- `Tools/AiContextMcp/**` read-only stdio MCP server template.
- Client configs for selected clients.

Add `--agents` to `init` or `bootstrap` to also generate versionable agent and instruction files:

```text
.github/copilot-instructions.md
.github/instructions/*.instructions.md
.github/agents/*.agent.md
.github/prompts/*.prompt.md
AGENTS.md
```

These files are intended to be committed. Existing unmanaged files in these locations are reported for manual review and are not overwritten by default. Managed files that still match the previous generated content can be safely updated. Managed files with user customization are reported for manual review unless an explicit force option is used.

The `.github/agents/*.agent.md` files follow VS Code custom agent conventions with frontmatter such as `name`, `description`, `target`, `tools`, and `handoffs` where useful. VS Code can use these agent files directly. Claude, Codex, and other assistants can still benefit from `AGENTS.md`, `.github/copilot-instructions.md`, and `.ai` guidance even when their native agent file format differs.

## Profiles

`--profile` controls which optional agent, instruction, and prompt templates are planned or generated when `--agents` is selected. The default profile is `generic`.

Supported profiles:

| Profile | Agents | Instructions | Prompts |
| --- | --- | --- | --- |
| `generic` | `ask`, `plan`, `implementer`, `reviewer`, `test-fixer` | `csharp`, `tests`, `safe-change` | `review-risk`, `fix-bug`, `generate-tests` |
| `dotnet` | generic plus `security-reviewer`, `source-generator-specialist` | generic plus `source-generator` | generic plus `migration-plan`, `analyze-source-generator` |
| `aspnet-core` | dotnet plus `api-reviewer` | dotnet plus `aspnet-core` | dotnet plus `review-api-change` |
| `legacy-dotnet` | dotnet plus `migration-architect` | dotnet plus `legacy-dotnet` | dotnet plus `migration-plan` |
| `winforms` | legacy-dotnet plus `winforms-specialist` | legacy-dotnet plus `winforms` | legacy-dotnet prompts |
| `oracle-datalayer` | dotnet plus `datalayer-specialist` | dotnet plus `oracle-datalayer` | dotnet plus `review-datasource-flow` |
| `demo` | broad demonstration set for API, migration, source-generator, security, and WinForms work | broad demonstration set | demonstration prompt set including source-generator, API, and migration prompts |

`demo` composes useful guidance from `dotnet`, `aspnet-core`, `legacy-dotnet`, WinForms, source-generator, and general repository safety concerns. The profile system remains explicit and deterministic; repository type and language detection is reported for visibility and future roadmap work, but it does not auto-select profiles.

VS Code and Copilot agents should use the generated instructions to query MCP server `ai_repo_context` before opening many source files. For `ai-repo:` requests, start with `get_repo_brief`, `get_health`, `get_context` for a context-packs brief, and `search_context` with the user task; then inspect only the files needed for the task. The shortcut is guidance for assistants only and does not add CLI prompt translation or new MCP tools in v1.0.0.

Recommended manual order:

```powershell
airepo plan --repo <target> --clients codex,vscode,vs --mcp
airepo init --repo <target> --clients codex,vscode,vs --mcp
airepo init --repo <target> --clients codex,vscode,vs --mcp --apply --backup
airepo validate --repo <target>
dotnet build <target>/Tools/AiContextMcp/AiRepo.ContextMcp.csproj -c Release
powershell -ExecutionPolicy Bypass -File <target>/Tools/AiContext/UpdateAiContext.ps1
powershell -ExecutionPolicy Bypass -File <target>/Tools/AiContext/CheckSdkAlignment.ps1
airepo code-index --repo <target> --apply
powershell -ExecutionPolicy Bypass -File <target>/Tools/AiContext/CheckSecrets.ps1
powershell -ExecutionPolicy Bypass -File <target>/Tools/AiContext/MeasureMcpResponseBudget.ps1
```

`MeasureMcpResponseBudget.ps1` starts the generated Release MCP DLL over JSON-RPC stdio, calls the read-only MCP tools, and writes `.ai/generated/reports/mcp-budget-report.json` plus `.ai/generated/reports/mcp-budget-report.md`.

## v0.8 Code Index Roslyn Lite

`airepo code-index` generates `.ai/generated/inventories/symbol-inventory.json`, `.ai/generated/inventories/symbol-inventory.md`, `.ai/generated/inventories/endpoint-inventory.json`, and `.ai/generated/inventories/endpoint-inventory.md` with Roslyn syntax parsing. It reads `.cs` files and uses `CSharpSyntaxTree.ParseText`, so it does not require restoring or building the target repository and does not use `MSBuildWorkspace`.

By default, code-index uses a local cache at `.ai/generated/cache/code-index-cache.json`. The cache is compact JSON, local to the target repo, regenerable, and ignored by git through the generated `.ai/generated/` ignore rule. When `code-index --apply` writes under `.ai/generated/`, it first ensures the AiRepoKit `.gitignore` section exists even if `init` or `bootstrap` was not applied. Each cache entry stores the relative file path, SHA-256, size, last write time UTC, symbols, and endpoints. Unchanged files are reused on later runs, changed files are parsed again, and deleted files are removed from the next cache write.

Dry-run is the default. Use `--apply` to write files:

```powershell
airepo code-index --repo . --apply
airepo code-index --apply
airepo code-index --apply --format json
airepo code-index --apply --rebuild-cache
airepo code-index --apply --no-cache
```

Options:

```text
--repo <path>
--apply
--dry-run
--max-files <number>
--max-items <number>
--include-private-members
--no-cache
--rebuild-cache
--output <path>
--format json|markdown|all
--verbose
--no-progress
```

Defaults are `--max-files 3000`, `--max-items 10000`, `--output .ai/generated/inventories`, `--format all`, and cache enabled. `--no-cache` does not read the existing cache and does not write a cache for that run. `--rebuild-cache` ignores the existing cache and writes a fresh cache when `--apply` is active. New writes must stay under `.ai/generated/`.

The inventory is syntax-based and compact. It detects namespaces, classes, records, interfaces, enums, structs, visibility, line numbers, attributes, base lists, public methods, constructors, public properties, parent symbols, partial/static/abstract/sealed modifiers, generic arity, controller endpoints, and minimal API endpoints. It classifies symbols as Controller, MinimalApi, Service, Repository, Handler, Dto, DbContext, Entity, Interface, Enum, Record, Middleware, Configuration, or Unknown.

RoslynLite reduces tokens because Codex, Copilot, and local LLMs can ask MCP for structural context before opening source files. The generated MCP server still exposes only 5 tools. `get_context` supports `kind=symbols` and `kind=endpoints` so agents can inspect repository structure and API surface in brief form.

The legacy `Tools/AiContext/UpdateCodeInventory.ps1` remains as a fallback heuristic generator. Bootstrap uses it only when the internal RoslynLite index fails.

Client configurations use the Release DLL:

```text
Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll
```

## v0.9 Context Packs

`airepo context-pack` generates compact task-oriented packs under `.ai/generated/context-packs/`. Packs are local, regenerable, and ignored by git through the `.ai/generated/` ignore rule. When `context-pack --apply` writes packs, it first ensures the AiRepoKit `.gitignore` section exists even if `init` or `bootstrap` was not applied. They are built from generated inventories and redacted reports, do not include source method bodies, and do not expose raw secrets or local machine paths.

Dry-run is the default:

```powershell
airepo context-pack --repo .
airepo context-pack --repo . --task change-api --target User
airepo context-pack --repo . --task change-api --target User --apply
airepo context-pack --repo . --task fix-build --apply --format json
airepo context-pack --repo . --task review-risk --apply --limit 20
```

Supported tasks are `change-api`, `change-ui`, `fix-build`, `update-package`, `review-risk`, `security-review`, and `test-generation`.

Defaults are `--task review-risk`, `--format all`, `--limit 20`, and output directory `.ai/generated/context-packs/`. When `--target` is supplied, the target is sanitized into the file name, for example `.ai/generated/context-packs/change-api.user.json` and `.ai/generated/context-packs/change-api.user.md`.

Without `--apply`, the command prints the planned pack summary and file names only. With `--apply`, it writes JSON and Markdown according to `--format`. The command ensures the RoslynLite code index exists unless `--skip-code-index` is passed; use `--rebuild-index` to refresh it first.

MCP keeps the same five tools. Use `get_context` to inspect generated packs:

```text
get_context kind=context-packs detail=brief
get_context kind=context-pack detail=compact task=change-api target=User
```

Brief mode lists available packs with task, target, summary, token budget hint, and suggested MCP calls. Compact mode returns selected pack contents within the existing MCP budget.

## v1.0 Efficiency Report

`airepo efficiency` prints a local report that estimates how much context can be saved by using the generated AiRepoKit MCP summaries and context packs before opening raw source files. Aliases are `airepo token-report` and `airepo context-efficiency`.

```powershell
airepo efficiency
airepo efficiency --repo .
airepo efficiency --repo . --json
airepo efficiency --repo . --no-refresh --no-progress
airepo efficiency --repo . --refresh
airepo efficiency --repo . --refresh --rebuild-index
airepo efficiency --repo . --skip-budget
```

`--repo` is optional. When omitted, the command uses the same repository resolver as the other commands: it accepts the executable directory when it looks like a target repository, otherwise it falls back to the current working directory. From a terminal, run it from the repository root to report on that repository. A copied standalone executable can also report on its current directory and will show a warning if repository data is minimal.

By default, the command uses smart refresh. It checks whether `.ai/generated/cache/code-index-cache.json`, `symbol-inventory.json`, and `endpoint-inventory.json` are missing or stale, refreshes the RoslynLite code index incrementally when needed, ensures `review-risk` and `fix-build` context packs exist, and tries to run `Tools/AiContext/MeasureMcpResponseBudget.ps1` when available. Refresh failures are warnings; the report continues with the best available local data.

Options:

```text
--repo <path>        Optional target repository.
--profile <name>     Profile label to include in the report.
--sample-query <q>   Sample task query. Default: architecture services controllers data access.
--json               Emit parseable JSON.
--no-progress        Disable progress on stderr.
--verbose            Emit more progress detail when progress is enabled.
--refresh            Force safe refresh of code-index, context packs, and budget data.
--no-refresh         Do not run refresh steps; read existing generated files only.
--rebuild-index      Rebuild the code-index cache during refresh.
--skip-budget        Do not run MeasureMcpResponseBudget.ps1; use an existing report or fallback estimate.
```

The token estimate is intentionally simple: bytes divided by `4`, rounded up. Raw source bytes include `.cs`, `.csproj`, `.sln`, `.slnx`, `.props`, and `.targets` files, excluding build, VCS, IDE, generated, artifact, temp, and dependency directories. Compact context bytes use generated inventories, context packs, and the MCP budget report. When the MCP budget report is available, its measured per-call response bytes are preferred for the compact-context token estimate.

This is an approximation, not exact tokenizer output. It is meant to make the savings visible to non-expert users and to compare raw repository reading with the local MCP/context-pack path. It does not print file contents, secret values, audit finding previews, or security report details. If the budget report says `secretsExposed` or `secretValuesReturned` is true, the report surfaces that as a warning; if no budget report exists, safety flags are shown as unknown.

JSON output includes the same report fields:

```powershell
airepo efficiency --repo . --json
```

## Generated Outputs

AiRepoKit keeps semantic context and executable tooling separated:

- `.ai/`: AI and human context, manifests, playbooks, budgets, and client snippets.
- `.ai/policies/`: versionable repository policies such as `audit-baseline.json`.
- `Tools/AiContext/`: executable repository-local scripts.
- `Tools/AiContextMcp/`: generated read-only MCP project source.
- `.ai/generated/`: local regenerable inventories, reports, and summaries.

The generated structure is:

```text
.ai/generated/
  cache/
  context-packs/
  inventories/
  reports/
  summaries/
```

These files are safe to regenerate and are ignored by the generated `.gitignore` section. AiRepoKit does not ignore application upload or data folders by default because those paths are application-specific and must stay under the target repository's own source-control policy.

## Audit

`airepo audit` scans repository files for local path leaks, explicit personal or AiRepoKit sandbox development names, likely secrets, Portuguese user-facing text, and legacy generated artifact candidates. It ignores local/generated folders such as `.ai/generated/`, `.dotnet-home/`, `artifacts/`, `.tmp/`, `bin/`, `obj/`, `node_modules/`, `.vs/`, and `.idea/`. Normal project, namespace, product, and repository names are not high-severity `PilotName` findings by default. Findings are matched against the versionable baseline file `.ai/policies/audit-baseline.json` by category, normalized relative file path, and a SHA-256 hash of the already redacted preview so raw secrets are never written to the baseline.

Baseline entries use these review states:

- `review-required`: the finding still blocks until a human reviews it.
- `accepted`: the finding is known and allowed until an optional expiration date.
- `false-positive`: the finding is known to be safe until an optional expiration date.

AiRepoKit does not auto-accept findings. New or merged baseline entries are always written as `review-required` so repositories keep an explicit review trail.

```powershell
airepo audit --repo .
airepo audit --repo . --baseline
airepo audit --repo . --create-baseline
airepo audit --repo . --create-baseline --update-baseline
airepo audit --repo . --fail-on-accepted
airepo audit --repo . --json
airepo audit --repo . --no-progress
airepo audit --repo . --include-source --verbose
```

Default audit behavior loads `.ai/policies/audit-baseline.json` when present. Accepted and false-positive findings remain visible in the report, but they do not count as active high-severity blockers unless `--fail-on-accepted` is provided or the baseline entry is expired. Missing baseline files are valid and mean no findings are accepted. Corrupt baseline files are fatal audit errors with exit code `1`.

`--create-baseline` writes `.ai/policies/audit-baseline.json` from the current findings and fails if the file already exists unless `--update-baseline` is also provided. `--update-baseline` merges newly discovered findings into the existing file as `review-required` entries and preserves existing statuses, reasons, and expiration values. `--baseline` prints the baseline summary alongside the audit summary.

Recommended repository flow:

1. `airepo audit --repo .`
2. `airepo audit --repo . --create-baseline`
3. Manually review `.ai/policies/audit-baseline.json`.
4. Change safe findings to `accepted` or `false-positive`.
5. Rerun `airepo audit --repo .`.
6. Rerun `airepo self-check --repo . --agents`.

Exit code `0` means no active high-severity findings remain, `2` means active high-severity findings were found, and `1` means a fatal audit error occurred. Secret pattern detection remains strict, while Portuguese text and legacy generated artifact findings are warnings so release validation can adopt the audit without blocking on copy cleanup.

## Gitignore Policy

During `init` and `bootstrap`, AiRepoKit creates or updates `.gitignore` with this idempotent section:

```text
# AiRepoKit local/generated artifacts
.ai/generated/
.dotnet-home/
.codex/config.toml
Tools/AiContextMcp/bin/
Tools/AiContextMcp/obj/
artifacts/
.tmp/
/airepo.exe
/airepo
/install-ai-context.cmd
/install-ai-context.ps1
```

Existing `.gitignore` content is preserved. If the section already exists it is not duplicated, and missing AiRepoKit local/generated rules are added to the existing section. Existing `.gitignore` files require `--backup` or `--force` during `init` and `bootstrap` before the section is appended. `code-index --apply` and `context-pack --apply` automatically ensure the same section before writing `.ai/generated/` outputs, because those outputs are always local and regenerable. `.ai/policies/` is not ignored so repository-owned policy files such as `.ai/policies/audit-baseline.json` can be committed. `.codex/config.toml` is local because Codex currently needs concrete paths. The versionable Codex snippet is written to `.ai/client-configs/codex.config.toml`. `.vscode/mcp.json` uses `${workspaceFolder}` and can be versioned. AiRepoKit does not add application upload or data folders, such as `src/Server/wwwroot/uploads/`, to `.gitignore` by default.

## Dry-Run Default

`airepo init`, `airepo sample`, and `airepo bootstrap` do not write files unless `--apply` is provided. `--dry-run` always forces planning mode, even if combined with `--apply`.

Examples:

```powershell
airepo plan --repo .
airepo init --repo . --clients codex,vscode,vs --mcp --profile dotnet
airepo init --repo . --clients codex,vscode,vs --mcp --profile dotnet --apply --backup
airepo doctor --repo . --target-framework net10.0 --profile dotnet
```

## Safety Policy

The CLI validates all write paths against the target repository root and refuses restricted destinations such as `.git`, `bin`, `obj`, `.vs`, `appsettings*.json`, Docker Compose files, key and certificate files, migrations, upload folders, and local data folders.

Existing files are not overwritten unless one of these options is explicit:

- `--backup`: create a timestamped `.bak` file before overwrite.
- `--force`: overwrite without backup.

The templates are generic and parameterized. They do not include repository-specific pilot content, secrets, global client config writes, HTTP endpoints, Resources, Prompts, persistence, server startup scripts, Docker orchestration, migrations, SQL, or database commands.
