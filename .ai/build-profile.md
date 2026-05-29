# Build Profile

## Repository

- Root: `<target-repo>`
- Main solution: `AI.RepoKit.MCP.sln`
- Target framework: `net10.0`

## Recommended Flow

1. Restore packages.
2. Build the main solution or the smallest affected project.
3. Run focused tests when available.

## Constraints

Do not start servers, Docker, migrations, SQL commands, or database tools as part of build diagnostics.
