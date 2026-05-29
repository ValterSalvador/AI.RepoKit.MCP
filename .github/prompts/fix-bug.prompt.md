# Fix Bug Prompt

Use `ai_repo_context` first: `get_repo_brief`, `get_health`, symbols brief via `get_context`, and `search_context`.

Reproduce the issue from available evidence, identify the smallest failing path, and apply a focused fix. Add or update tests when practical.

Do not read secrets. Do not run app servers, Docker, migrations, SQL, or database commands. Inspect generated outputs only under `.ai/generated`.
