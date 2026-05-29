# MCP Context Manifest

The manifest defines the read surface for the local MCP server.

## Rules

- Prefer `.ai/manifests/mcp-context-manifest.json`.
- `.ai/mcp-context-manifest.json` is accepted for compatibility.
- Read only files listed in `allowedContextFiles`.
- Block restricted paths and reparse points.
- Return redacted values only when sensitive patterns are detected.

## Generated Inventories And Reports

- `.ai/generated/inventories/project-inventory.json`
- `.ai/generated/inventories/project-references.json`
- `.ai/generated/inventories/package-inventory.json`
- `.ai/generated/inventories/sdk-inventory.json`
- `.ai/generated/inventories/symbol-inventory.json`
- `.ai/generated/inventories/symbol-inventory.md`
- `.ai/generated/inventories/endpoint-inventory.json`
- `.ai/generated/inventories/endpoint-inventory.md`
- `.ai/generated/reports/mcp-budget-report.json`
- `.ai/generated/reports/mcp-budget-report.md`
- `.ai/generated/reports/sdk-alignment-report.json`
- `.ai/generated/reports/secret-scan-report.json`
- `.ai/generated/reports/build-diagnostics-report.json`
- `.ai/generated/reports/latest-build-summary.json`
- `.ai/generated/summaries/generated-context-summary.md`

These files are local and regenerable. The MCP server reads the generated paths first and keeps compatibility fallback for older `.ai/*.json` inventory and report paths.
