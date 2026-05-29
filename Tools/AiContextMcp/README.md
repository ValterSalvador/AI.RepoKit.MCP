# AiRepo.ContextMcp

Generic read-only MCP context server for a local repository. It uses the stable `ModelContextProtocol` package and stdio transport.

## Transport

- stdio only.
- stdout is reserved for the MCP protocol.
- logs go to stderr.

## SDK

- Package: `ModelContextProtocol`
- Version: `1.3.0`
- ASP.NET Core transport package: not used.

## Scope

The server reads `.ai/manifests/mcp-context-manifest.json` first and falls back to `.ai/mcp-context-manifest.json`. It reads only `allowedContextFiles`, blocks restricted paths and reparse points, and returns redacted values only.

## Build

```powershell
dotnet build Tools/AiContextMcp/AiRepo.ContextMcp.csproj -c Release
```
