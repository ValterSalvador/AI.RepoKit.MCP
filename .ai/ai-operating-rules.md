# AI Operating Rules

## Defaults

- Work read-only until a human explicitly asks for changes.
- Prefer small, reviewable edits.
- Preserve dry-run behavior in tools and scripts.
- Do not execute servers, Docker, migrations, SQL, or database commands from automation.
- Do not read or copy secrets.
- Keep generated context generic and repository-local.

## Restricted Content

Never include values from secret stores, environment files, key files, certificates, app settings, local databases, dumps, generated binaries, or runtime output folders.

## Write Rules

Existing files require an explicit backup or force option before overwrite.
