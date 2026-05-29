# SDK Profile

## Defaults

- Target framework: `{{TargetFramework}}`
- Main solution: `{{MainSolution}}`

## Checks

Use `Tools/AiContext/CheckSdkAlignment.ps1` to compare project target frameworks and local SDK discovery.

## Notes

Do not change `global.json`, project target frameworks, or package versions without an explicit task.
