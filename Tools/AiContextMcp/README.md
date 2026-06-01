# AiRepo.ContextMcp

Generic read-only MCP context server for a local repository. It uses the stable `ModelContextProtocol` package and stdio transport. v1.5.0 keeps the compact tool surface and adds MCP Resources and Prompts for discoverability.

## Transport

- stdio only.
- stdout is reserved for JSON-RPC only.
- stderr is silent by default during MCP operation.
- logs go to `%TEMP%/ai-repo-context-mcp.log` by default.
- pass `--debug` or `--verbose` only when you explicitly want stderr logging for local troubleshooting.
- pass `--log-file <path>` to override the file log path.

## SDK

- Package: `ModelContextProtocol`
- Version: `1.3.0`
- ASP.NET Core transport package: not used.

## Scope

The server reads `.ai/manifests/mcp-context-manifest.json` first and falls back to `.ai/mcp-context-manifest.json`. It reads only `allowedContextFiles`, blocks restricted paths and reparse points, and returns redacted values only.

The server policy is read-only: no file writes, command execution, database access, or network access. `get_policy` returns the explicit policy, allowed root, denied paths, generated artifact paths, and safe suggested-command rules.

## Capabilities

Use `get_health area=capabilities` to get server/tool version, repository root, available tools, supported context kinds, generated artifact availability, missing generated artifacts, supported policies, read-only mode, recommended detail level, default budgets, cheap client config detection, resource URIs, prompt names, and strict stdio defaults.

## Tools

- `get_repo_brief`
- `get_health`
- `get_policy`
- `get_context`
- `search_context`

## Resources

Resources are read-only, redacted, budget-aware content entrypoints exposed through `resources/list` and `resources/read`.

- `repo://brief`
- `repo://health`
- `repo://policy`
- `repo://context/changed-files`
- `repo://context/review-risk`
- `repo://context/test-generation`
- `repo://graph/dependencies`
- `repo://impact/current`
- `repo://org/report`

## Prompts

Prompts are short reusable workflows exposed through `prompts/list` and `prompts/get`.

- `ai-repo.help`
- `ai-repo.tutorial-en`
- `ai-repo.tutorial-pt`
- `ai-repo.token-efficiency-check`
- `ai-repo.review-risk`
- `ai-repo.changed-files-review`
- `ai-repo.generate-tests`
- `ai-repo.before-commit`
- `ai-repo.implementation-plan`
- `ai-repo.release-check`

Operational prompts instruct agents to start with `get_repo_brief`, `get_health area=capabilities`, `get_policy`, `get_context kind=changed-files detail=brief`, and focused `search_context` calls before direct file inspection.

## Structured Errors

Recoverable missing or oversized artifacts return structured tool payloads instead of JSON-RPC protocol errors:

```json
{
  "ok": false,
  "code": "CONTEXT_NOT_FOUND",
  "message": "Context artifact was not generated.",
  "suggestedCommand": "airepo context-pack --apply",
  "safeToRun": true,
  "details": {}
}
```

## Build

```powershell
dotnet build Tools/AiContextMcp/AiRepo.ContextMcp.csproj -c Release
```
