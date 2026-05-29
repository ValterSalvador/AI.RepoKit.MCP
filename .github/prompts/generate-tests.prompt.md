# Generate Tests Prompt

Use `ai_repo_context` first: `get_repo_brief`, `get_health`, `get_context` symbols brief, and `search_context`.

Generate focused tests for the behavior under change. Prefer existing test frameworks, helpers, fixture style, and naming conventions.

Do not read secrets. Do not run app servers, Docker, migrations, SQL, or database commands. Inspect generated outputs only under `.ai/generated`.
