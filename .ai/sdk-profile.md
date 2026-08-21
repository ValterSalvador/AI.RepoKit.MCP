# SDK Profile

## Defaults

- Target framework: `net10.0`
- Main solution: `AI.RepoKit.MCP.sln`

## Checks

Use `airepo sdk-alignment --repo .` for native SDK alignment. `CheckSdkAlignment.ps1` and `CheckSdkAlignment.sh` remain thin compatibility wrappers.

## Notes

Do not change `global.json`, project target frameworks, or package versions without an explicit task.
