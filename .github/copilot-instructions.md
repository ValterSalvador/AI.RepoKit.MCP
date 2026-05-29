# Copilot Instructions

Use repository context before opening many files. Prefer the MCP server `ai_repo_context`:

- `get_repo_brief`
- `get_health`
- `get_context` with `kind=symbols` and `detail=brief`
- `search_context`

Safety rules:

- Do not read secrets or local credentials.
- Do not run app servers, Docker, migrations, SQL, or database commands.
- Prefer a plan before applying broad changes.
- Inspect generated outputs only under `.ai/generated`.
- Keep changes small, tested, and consistent with existing patterns.
