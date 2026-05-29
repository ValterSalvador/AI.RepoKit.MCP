# Automation Risks

## High Risk

- Database changes, migrations, SQL execution, or data repair.
- Secret, credential, certificate, token, or key handling.
- Production deployment, infrastructure mutation, or remote service changes.
- Long-running servers, Docker orchestration, or background daemons.

## Medium Risk

- Package upgrades.
- Build pipeline edits.
- Broad refactors across multiple projects.

## Default Handling

Plan first, keep changes scoped, and request human approval before any operation that can mutate external state.
