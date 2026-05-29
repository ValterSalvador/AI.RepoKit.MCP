{
  "mcpServers": {
    "{{McpServerName}}": {
      "command": "dotnet",
      "args": [
        "<target-repo>/Tools/AiContextMcp/bin/Release/{{TargetFramework}}/{{McpAssemblyName}}.dll",
        "--repo",
        "<target-repo>"
      ],
      "cwd": "<target-repo>"
    }
  }
}
