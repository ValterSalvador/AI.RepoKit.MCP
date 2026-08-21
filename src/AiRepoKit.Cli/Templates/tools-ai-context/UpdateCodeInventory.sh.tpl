#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/../.." && pwd -P)"
forward=()

while (($# > 0)); do
  case "$1" in
    --repo)
      if (($# < 2)); then
        echo "--repo requires a path." >&2
        exit 1
      fi

      repo_root="$(cd -- "$2" && pwd -P)"
      shift 2
      ;;
    *)
      forward+=("$1")
      shift
      ;;
  esac
done

command_args=("code-index" "--repo" "$repo_root" "--apply" "--max-files" "2000" "--max-items" "5000")
command_args+=("${forward[@]}")

if dotnet tool run airepo -- --version >/dev/null 2>&1; then
  exec dotnet tool run airepo -- "${command_args[@]}"
fi

if command -v airepo >/dev/null 2>&1; then
  exec airepo "${command_args[@]}"
fi

echo "airepo was not found. Restore the local dotnet tool or install AiRepoKit.Cli." >&2
exit 1
