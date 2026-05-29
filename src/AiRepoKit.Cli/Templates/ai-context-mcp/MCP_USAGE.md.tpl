# MCP Usage

Build the server in Release mode before configuring clients. The template uses stdio and the stable `ModelContextProtocol` package version `1.3.0`.

```powershell
dotnet build {{McpProjectRelativePath}} -c Release
```

Client configs should execute:

```text
dotnet Tools/AiContextMcp/bin/Release/{{TargetFramework}}/{{McpAssemblyName}}.dll --repo <target-repo>
```

Available tools:

- `get_repo_brief`
- `get_context`
- `get_health`
- `search_context`
- `get_policy`
