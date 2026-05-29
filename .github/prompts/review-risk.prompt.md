# Review Risk Prompt

Use `ai_repo_context` first: `get_repo_brief`, `get_health`, `get_context` with `kind=symbols` and `detail=brief`, then `search_context`.

Review the proposed change for regressions, missing tests, unsafe assumptions, and broad side effects. Prioritize concrete findings over summaries.

Do not read secrets. Do not run app servers, Docker, migrations, SQL, or database commands. Inspect generated outputs only under `.ai/generated`.
