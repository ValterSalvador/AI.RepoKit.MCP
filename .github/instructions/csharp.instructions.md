---
description: C# repository conventions for AI-assisted changes.
target: vscode
---

# C# Instructions

Start with `ai_repo_context`: `get_repo_brief`, `get_health`, `get_context` with `kind=context-packs` and `detail=brief`, and `search_context` with the user task. Prefer context packs and indexed summaries before opening files. Keep responses token-efficient, and do not run write, build, server, Docker, migration, SQL, or database commands unless the selected role and user permission allow it.

Follow existing project style, target framework, nullable settings, package policy, and public API patterns. Prefer simple local fixes over new abstractions unless they remove real duplication or complexity.

Do not read secrets or local credentials. Do not run app servers, Docker, migrations, SQL, or database commands. Inspect generated outputs only under `.ai/generated`.
