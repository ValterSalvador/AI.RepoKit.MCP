# Codex Usage Guide

## Recommended Prompt Shape

State the goal, affected area, constraints, and required verification. Mention whether edits are allowed.

## Safe Defaults

- Ask for a plan before broad changes.
- Use dry-run first for generated infrastructure.
- Require `--apply --backup` or `--force` for overwrites.

## Repository Commands

Run commands from the repository root without `--repo`. The CLI resolves the current directory and nearest Git root automatically, so generated instructions should prefer `airepo setup`, `airepo update`, and similar forms instead of adding `--repo .`. Use `--repo <path>` only for an explicitly different target.

Applied `setup` and `bootstrap` runs install the managed Git hooks by default. Add `--no-hooks` or `--skip-hooks` only when hook installation must be disabled.
## MCP

The local MCP server is read-only-first and reads only files listed in the context manifest.
