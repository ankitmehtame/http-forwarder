#!/usr/bin/env bash
set -euo pipefail

IMAGE_NAME="${1:-http-forwarder-app}"
IMAGE_TAG="${2:-}"
TOOL_DIR="${TMPDIR:-/tmp}/http-forwarder-nbgv-tool"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$repo_root"

ensure_nbgv() {
  if command -v nbgv >/dev/null 2>&1; then
    command -v nbgv
    return 0
  fi

  if [[ -x "$TOOL_DIR/nbgv" ]]; then
    printf '%s\n' "$TOOL_DIR/nbgv"
    return 0
  fi

  mkdir -p "$TOOL_DIR"
  if dotnet tool install --tool-path "$TOOL_DIR" nbgv >/dev/null; then
    printf '%s\n' "$TOOL_DIR/nbgv"
    return 0
  fi

  return 1
}

read_version_json_version() {
  sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' version.json | head -n 1
}

get_nbgv_value() {
  local nbgv_cmd="$1"
  local variable="$2"
  "$nbgv_cmd" get-version -v "$variable"
}

sanitize_docker_tag() {
  printf '%s' "$1" | tr -c 'A-Za-z0-9_.-' '-'
}

if nbgv_cmd="$(ensure_nbgv)"; then
  VERSION="$(get_nbgv_value "$nbgv_cmd" SemVer2)"
  ASSEMBLY_VERSION="$(get_nbgv_value "$nbgv_cmd" AssemblyVersion)"
  FILE_VERSION="$(get_nbgv_value "$nbgv_cmd" AssemblyFileVersion)"
  INFORMATIONAL_VERSION="$(get_nbgv_value "$nbgv_cmd" AssemblyInformationalVersion)"
else
  echo "Warning: nbgv is not available and could not be installed. Falling back to version.json plus git commit." >&2
  base_version="$(read_version_json_version)"
  base_version="${base_version:-0.0}"
  short_commit="$(git rev-parse --short=10 HEAD 2>/dev/null || printf 'unknown')"
  VERSION="${base_version}.0-local.${short_commit}"
  ASSEMBLY_VERSION="${base_version}.0.0"
  FILE_VERSION="$ASSEMBLY_VERSION"
  INFORMATIONAL_VERSION="${base_version}.0+${short_commit}"
fi

APP_COMMIT="$(git rev-parse HEAD 2>/dev/null || printf 'unknown')"
APP_BUILD_ID="local"
IMAGE_TAG="${IMAGE_TAG:-$(sanitize_docker_tag "$VERSION")}"

echo "Building $IMAGE_NAME:$IMAGE_TAG and $IMAGE_NAME:local"
echo "VERSION=$VERSION"
echo "ASSEMBLY_VERSION=$ASSEMBLY_VERSION"
echo "FILE_VERSION=$FILE_VERSION"
echo "INFORMATIONAL_VERSION=$INFORMATIONAL_VERSION"
echo "APP_COMMIT=$APP_COMMIT"

docker build \
  --build-arg "VERSION=$VERSION" \
  --build-arg "ASSEMBLY_VERSION=$ASSEMBLY_VERSION" \
  --build-arg "FILE_VERSION=$FILE_VERSION" \
  --build-arg "INFORMATIONAL_VERSION=$INFORMATIONAL_VERSION" \
  --build-arg "APP_BUILD_ID=$APP_BUILD_ID" \
  --build-arg "APP_COMMIT=$APP_COMMIT" \
  -t "$IMAGE_NAME:$IMAGE_TAG" \
  -t "$IMAGE_NAME:local" \
  .
