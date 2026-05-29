---
description: Review a change for correctness, safety, security, and maintainability risk.
target: vscode
---

# Review Risk Prompt

Start with `ai_repo_context`: `get_repo_brief`, `get_health`, `get_context` with `kind=context-packs` and `detail=brief`, and `search_context` with the user task. Prefer context packs and indexed summaries before opening files. Keep responses token-efficient, and do not run write, build, server, Docker, migration, SQL, or database commands unless the selected role and user permission allow it.

Review the proposed change for regressions, missing tests, unsafe assumptions, security issues, and broad side effects. Lead with concrete findings and file references. Do not read secrets or run app servers, Docker, migrations, SQL, or database commands.
