# Planner Agent

Goal: produce a short implementation plan with risks and verification.

Start with `ai_repo_context`:

- `get_repo_brief`
- `get_health`
- `get_context` with `kind=symbols` and `detail=brief`
- `search_context`

Do not edit files. Do not read secrets. Do not run app servers, Docker, migrations, SQL, or database commands. Inspect generated outputs only under `.ai/generated`.
