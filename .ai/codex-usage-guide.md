# Codex Usage Guide

## Recommended Prompt Shape

State the goal, affected area, constraints, and required verification. Mention whether edits are allowed.

## Safe Defaults

- Ask for a plan before broad changes.
- Use dry-run first for generated infrastructure.
- Require `--apply --backup` or `--force` for overwrites.

## MCP

The local MCP server is read-only-first and reads only files listed in the context manifest.
