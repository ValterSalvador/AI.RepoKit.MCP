# MCP Usage

Build the server in Release mode before configuring clients. The template uses stdio and the stable `ModelContextProtocol` package version `1.3.0`.

```powershell
dotnet build Tools/AiContextMcp/AiRepo.ContextMcp.csproj -c Release
```

Client configs should execute:

```text
dotnet Tools/AiContextMcp/bin/Release/net10.0/AiRepo.ContextMcp.dll --repo <target-repo>
```

Default operation is strict stdio friendly: stdout is JSON-RPC only, stderr is silent, and logs are written to `%TEMP%/ai-repo-context-mcp.log`. Use `--debug` or `--verbose` only when you explicitly want stderr logs. Use `--log-file <path>` to override the log file.

Available tools:

- `get_repo_brief`
- `get_context`
- `get_health`
- `search_context`
- `get_policy`

Available resources:

- `repo://brief`
- `repo://health`
- `repo://policy`
- `repo://context/changed-files`
- `repo://context/review-risk`
- `repo://context/test-generation`
- `repo://graph/dependencies`
- `repo://impact/current`
- `repo://org/report`

Available prompts:

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

Recommended startup calls:

```text
get_repo_brief detail=brief
get_health area=capabilities
get_policy topic=all
get_context kind=changed-files detail=brief limit=5
search_context query="<task keywords>" limit=10
```

Agents may also read `repo://brief`, `repo://health`, and `repo://context/changed-files` first when their MCP client exposes resources. Use `prompts/get ai-repo.help` for compact built-in guidance, `ai-repo.tutorial-en` or `ai-repo.tutorial-pt` for onboarding, and `ai-repo.token-efficiency-check` before broad file inspection.

Recoverable failures use structured payloads:

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

Use `airepo mcp-diagnose --strict-stdio` to verify initialize, tools/list, resources/list, resources/read `repo://brief`, prompts/list, prompts/get `ai-repo.help`, prompts/get `ai-repo.review-risk`, minimal safe calls to the core tools, and empty stderr with stderr line and byte counts.
