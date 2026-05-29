# C# Instructions

Start with `ai_repo_context` before reading source broadly:

- `get_repo_brief`
- `get_health`
- `get_context` with `kind=symbols` and `detail=brief`
- `search_context`

Follow existing project style, target framework, nullable settings, and package choices. Keep public API changes intentional. Prefer simple, local fixes over new abstractions unless duplication or complexity justifies them.

Do not read secrets. Do not run app servers, Docker, migrations, SQL, or database commands. Generated outputs may be inspected only under `.ai/generated`.
