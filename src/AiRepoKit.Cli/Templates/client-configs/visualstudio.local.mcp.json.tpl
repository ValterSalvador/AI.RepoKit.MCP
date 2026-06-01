{
  "servers": {
    "{{McpServerName}}": {
      "type": "stdio",
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
