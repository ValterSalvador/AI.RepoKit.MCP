# Test Instructions

Use `ai_repo_context` first to find relevant test and production symbols. Start with `get_repo_brief`, `get_health`, `get_context` for symbols brief, and `search_context`.

Add or update focused tests for changed behavior. Keep fixtures minimal and deterministic. Prefer existing test helpers and naming patterns.

Do not run app servers, Docker, migrations, SQL, or database commands. Do not read secrets. Inspect generated outputs only under `.ai/generated`.
