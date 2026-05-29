{
  "servers": {
    "{{McpServerName}}": {
      "transport": "stdio",
      "command": "dotnet",
      "args": [
        "{{McpDllPortable}}",
        "--repo",
        "{{RepoRootPortable}}"
      ],
      "cwd": "{{RepoRootPortable}}"
    }
  }
}
