# AI Context

Repository: `{{RepoName}}`

Generated: `{{GeneratedAtLocal}}`

This folder contains generic AI operating context for local assistants and read-only MCP tools. It is safe to commit when reviewed by maintainers.

## Files

- `project-map.md`: repository shape and important paths.
- `build-profile.md`: build entry points and constraints.
- `test-profile.md`: test expectations.
- `sdk-profile.md`: .NET SDK and target framework notes.
- `ai-operating-rules.md`: safety and collaboration rules.
- `automation-risks.md`: tasks that require extra care.
- `context-budget.json`: response and file budget defaults.
- `manifests/mcp-context-manifest.json`: allowed read surface for MCP.
- `generated/inventories/`: local regenerable inventories.
- `generated/reports/`: local regenerable reports.
- `generated/summaries/`: local regenerable summaries.

## Code Inventory Lite

`airepo code-index --apply` creates a lightweight structural inventory with RoslynLite syntax parsing under `.ai/generated/inventories/`. It does not require restoring or building the target repo, does not execute project code, and does not read restricted config, key, upload, build, Docker, or data paths. `Tools/AiContext/UpdateCodeInventory.ps1` remains a fallback heuristic generator. Use the inventory to reduce tokens by asking MCP for `get_context symbols brief` or `get_context endpoints brief` before opening source files.

## Safety

Do not place secrets, credentials, tokens, private keys, connection strings, dumps, generated binaries, logs, or environment-specific values in this folder. Files under `.ai/generated/` are local and regenerable.
