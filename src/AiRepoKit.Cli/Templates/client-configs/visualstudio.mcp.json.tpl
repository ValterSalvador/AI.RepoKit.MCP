{
  "servers": {
    "{{McpServerName}}": {
      "type": "stdio",
      "command": "{{ToolCommandName}}",
      "args": [
        "mcp",
        "serve",
        "--repo",
        "${workspaceFolder}"
      ],
      "cwd": "${workspaceFolder}"
    }
  }
}
