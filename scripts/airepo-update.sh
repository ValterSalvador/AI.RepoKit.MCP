#!/usr/bin/env bash
set -euo pipefail

RELEASE_REPO="${AIREPO_RELEASE_REPO:-}"
PACKAGE_ID="AiRepoKit.Cli"
COMMAND_NAME="airepo"
REPO_PATH="$(pwd)"
ROOT_PATH=""
VERSION="latest"
SOURCE="github"
ALL=0
APPLY=0
SETUP=0
CACHE="${TMPDIR:-/tmp}/airepo-update"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo)
      REPO_PATH="$2"; shift 2 ;;
    --root)
      ROOT_PATH="$2"; shift 2 ;;
    --version)
      VERSION="$2"; shift 2 ;;
    --source)
      SOURCE="$2"; shift 2 ;;
    --release-repo)
      RELEASE_REPO="$2"; shift 2 ;;
    --all)
      ALL=1; shift ;;
    --apply)
      APPLY=1; shift ;;
    --setup)
      SETUP=1; shift ;;
    --help|-h)
      cat <<EOF
AI RepoKit updater

Usage:
  ./airepo-update.sh
  ./airepo-update.sh --repo /path/to/repo
  ./airepo-update.sh --root /path/to/repos --all
  ./airepo-update.sh --root /path/to/repos --all --apply
  ./airepo-update.sh --version 1.4.2
  ./airepo-update.sh --version 1.4.2 --release-repo <owner>/<repo>

Notes:
  --all is dry-run by default.
  --apply is required to update multiple repos.
  GitHub downloads use --release-repo, AIREPO_RELEASE_REPO, or git remote origin.
EOF
      exit 0 ;;
    *)
      echo "Unknown argument: $1"
      exit 1 ;;
esac
done

remote_to_repo() {
  remote_url="${1:-}"
  remote_url="${remote_url%.git}"

  case "$remote_url" in
    *github.com:*)
      echo "${remote_url##*github.com:}"
      return 0 ;;
    *github.com/*)
      echo "${remote_url##*github.com/}"
      return 0 ;;
    */*/*)
      echo "${remote_url#*/}"
      return 0 ;;
    */*)
      echo "$remote_url"
      return 0 ;;
  esac

  return 1
}

derive_release_repo() {
  if [[ -n "${RELEASE_REPO:-}" ]]; then
    return 0
  fi

  remote=""
  if git -C "$REPO_PATH" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    remote="$(git -C "$REPO_PATH" config --get remote.origin.url || true)"
  elif git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
    remote="$(git config --get remote.origin.url || true)"
  fi

  if [[ -n "$remote" ]]; then
    RELEASE_REPO="$(remote_to_repo "$remote" || true)"
  fi
}

require_release_repo() {
  derive_release_repo

  if [[ -z "${RELEASE_REPO:-}" ]]; then
    echo "[FAIL] GitHub source requires a release repository. Pass --release-repo <owner>/<repo>, set AIREPO_RELEASE_REPO, or run from a checkout with git remote origin."
    exit 1
  fi
}

resolve_version() {
  if [[ "$VERSION" != "latest" ]]; then
    RESOLVED_VERSION="$VERSION"
    return
  fi

  require_release_repo

  RESOLVED_VERSION="$(
    curl -fsSL "https://api.github.com/repos/${RELEASE_REPO}/releases/latest" |
      sed -n 's/.*"tag_name"[[:space:]]*:[[:space:]]*"v\{0,1\}\([^"]*\)".*/\1/p' |
      head -n 1
  )"

  if [[ -z "${RESOLVED_VERSION:-}" ]]; then
    echo "[FAIL] Could not resolve latest GitHub release."
    exit 1
  fi
}

download_nupkg() {
  if [[ "$SOURCE" != "github" ]]; then
    echo "[FAIL] airepo-update.sh currently supports --source github only."
    exit 1
  fi

  resolve_version
  require_release_repo

  NUPKG_DIR="${CACHE}/${RESOLVED_VERSION}"
  mkdir -p "$NUPKG_DIR"

  asset_url="$(
    curl -fsSL "https://api.github.com/repos/${RELEASE_REPO}/releases/tags/v${RESOLVED_VERSION}" |
      grep -E '"browser_download_url":' |
      grep -E 'AiRepoKit\.Cli.*\.nupkg' |
      sed -E 's/.*"browser_download_url":[[:space:]]*"([^"]+)".*/\1/' |
      head -n 1
  )"

  if [[ -z "${asset_url:-}" ]]; then
    echo "[FAIL] No AiRepoKit.Cli nupkg asset found in release v${RESOLVED_VERSION}."
    exit 1
  fi

  NUPKG_PATH="${NUPKG_DIR}/$(basename "$asset_url")"
  curl -fL "$asset_url" -o "$NUPKG_PATH"
}

update_one() {
  download_nupkg

  echo
  echo "==> Updating repo: $REPO_PATH"

  if [[ ! -d "$REPO_PATH" ]]; then
    echo "[FAIL] Repo path not found: $REPO_PATH"
    exit 1
  fi

  pushd "$REPO_PATH" >/dev/null

  if [[ ! -f ".config/dotnet-tools.json" ]]; then
    echo "Creating local tool manifest..."
    dotnet new tool-manifest
  fi

  echo "Updating ${PACKAGE_ID} to ${RESOLVED_VERSION}..."
  if ! dotnet tool update "$PACKAGE_ID" --version "$RESOLVED_VERSION" --add-source "$NUPKG_DIR"; then
    echo "Update failed; trying install..."
    dotnet tool install "$PACKAGE_ID" --version "$RESOLVED_VERSION" --add-source "$NUPKG_DIR"
  fi

  dotnet tool restore
  dotnet tool run "$COMMAND_NAME" -- --version

  if [[ "$SETUP" == "1" ]]; then
    dotnet tool run "$COMMAND_NAME" -- setup --repo . --clients codex,vscode,vs --mcp --agents --profile auto --no-progress
  fi

  popd >/dev/null

  echo
  echo "[OK] Updated repo: $REPO_PATH"
}

update_all() {
  if [[ -z "$ROOT_PATH" ]]; then
    ROOT_PATH="$(pwd)"
  fi

  echo
  echo "==> Scanning repos under: $ROOT_PATH"
  echo "    Apply: $APPLY"
  echo

  mapfile -t repos < <(
    find "$ROOT_PATH" -type d \( -name .git -o -path '*/.config' \) -prune -print 2>/dev/null |
      sed -E 's#/.git$##; s#/.config$##' |
      sort -u
  )

  for repo in "${repos[@]}"; do
    if [[ "$APPLY" == "1" ]]; then
      "$0" --repo "$repo" --version "$VERSION"
    else
      echo "[DRY-RUN] Would update: $repo"
    fi
  done

  if [[ "$APPLY" == "0" ]]; then
    echo
    echo "Dry-run only. Add --apply to update all repos."
  fi
}

if [[ "$ALL" == "1" ]]; then
  update_all
else
  update_one
fi
