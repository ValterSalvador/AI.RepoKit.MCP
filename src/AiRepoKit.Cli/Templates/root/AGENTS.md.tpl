# Agent Guide

Agents should start with repository context from MCP server `ai_repo_context`:

- `get_repo_brief`
- `get_health`
- `get_context` with `kind=context-packs` and `detail=brief`
- `search_context`

Natural-language shortcuts:

- Treat `ai-repo: <natural language task>` as a request to use `ai_repo_context` first.
- Start with compact calls: `get_repo_brief`, `get_health`, `get_context` with `kind=context-packs` and `detail=brief`, and `search_context` with the user task.
- Prefer context packs and indexed summaries before opening files.
- Avoid broad file reads unless MCP context is insufficient.
- Keep responses token-efficient.
- Do not run write, build, server, Docker, migration, SQL, or database commands unless the selected agent role and user permission allow it.
- Optional sub-prefixes: `ai-repo ask:`, `ai-repo plan:`, `ai-repo fix:`, `ai-repo review:`, and `ai-repo test:`.

Safety:

- Do not read secrets or local credentials.
- Do not run app servers, Docker, migrations, SQL, or database commands.
- Prefer a plan before applying broad changes.
- Inspect generated outputs only under `.ai/generated`.
- Keep changes small, tested, and consistent with existing patterns.
