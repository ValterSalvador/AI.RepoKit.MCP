{
  "servers": {
    "{{McpServerName}}": {
      "type": "stdio",
      "command": "{{ToolCommandName}}",
      "args": [
        "mcp",
        "serve",
        "--repo",
        "{{RepoRootPortable}}"
      ],
      "cwd": "{{RepoRootPortable}}"
    }
  }
}
