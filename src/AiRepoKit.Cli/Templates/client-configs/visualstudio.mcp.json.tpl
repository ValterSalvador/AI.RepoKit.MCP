{
  "servers": {
    "{{McpServerName}}": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "${workspaceFolder}/Tools/AiContextMcp/bin/Release/{{TargetFramework}}/{{McpAssemblyName}}.dll",
        "--repo",
        "${workspaceFolder}"
      ],
      "cwd": "${workspaceFolder}"
    }
  }
}
