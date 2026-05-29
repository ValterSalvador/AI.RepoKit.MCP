# Safe Change Instructions

Begin with MCP context from `ai_repo_context`: `get_repo_brief`, `get_health`, `get_context` symbols brief, then `search_context`.

Before applying changes, state the intended scope and risk. Keep edits narrow. Preserve generated and user-customized files unless explicitly asked to regenerate them.

Do not read secrets. Do not run app servers, Docker, migrations, SQL, or database commands. Inspect generated outputs only under `.ai/generated`.
