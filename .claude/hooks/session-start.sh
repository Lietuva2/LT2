#!/bin/bash
# Claude Code cloud sessions only: install the .NET SDK that global.json pins and start Docker,
# so a cloud session can run `dotnet build` and `dotnet test` (unit, architecture, integration).
# Local sessions (the owner's PC) skip all of it.
#
# Why this way: the cloud network allows mcr.microsoft.com but not builds.dotnet.microsoft.com,
# and Ubuntu's own dotnet-sdk-10.0 is a 10.0.1xx SDK, not the pinned one. So the SDK is copied
# out of the official SDK container image's layers, fetched straight from the registry (no
# Docker needed). postgres:17 comes from mirror.gcr.io, because anonymous pulls from Docker Hub
# share an address and often get "429 Too Many Requests".
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-.}"
log=/tmp/lt2-session-start.log
dotnet_root=/usr/share/dotnet

sdk_version=$(python3 -I -c 'import json,sys; print(json.load(open(sys.argv[1]))["sdk"]["version"])' global.json)

has_sdk() {
  [ -x "$dotnet_root/dotnet" ] && "$dotnet_root/dotnet" --list-sdks 2>/dev/null | grep -q "^$sdk_version "
}

if ! has_sdk; then
  case "$(uname -m)" in
    x86_64) arch=amd64 ;;
    aarch64) arch=arm64v8 ;;
    *) echo "session-start: no .NET SDK image for $(uname -m)" >&2; exit 1 ;;
  esac
  registry=https://mcr.microsoft.com/v2/dotnet/sdk
  work=$(mktemp -d)
  trap 'rm -rf "$work"' EXIT
  curl -fsSL "$registry/manifests/$sdk_version-noble-$arch" \
    -H 'Accept: application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json' \
    -o "$work/manifest.json"
  mkdir -p "$work/root"
  # Every layer except the Ubuntu base; only usr/share/dotnet is kept.
  for digest in $(python3 -I -c 'import json,sys; [print(l["digest"]) for l in json.load(open(sys.argv[1]))["layers"][1:]]' "$work/manifest.json"); do
    curl -fsSL "$registry/blobs/$digest" -o "$work/layer.tgz"
    tar -xzf "$work/layer.tgz" -C "$work/root" --wildcards 'usr/share/dotnet/*' 2>/dev/null || true
    rm -f "$work/layer.tgz"
  done
  rm -rf "$dotnet_root"
  mv "$work/root/usr/share/dotnet" "$dotnet_root"
  ln -sf "$dotnet_root/dotnet" /usr/local/bin/dotnet
  has_sdk || { echo "session-start: .NET SDK $sdk_version did not install" >&2; exit 1; }
fi

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo "export DOTNET_ROOT=$dotnet_root"
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    # Ryuk (Testcontainers' clean-up container) is pulled from Docker Hub; the container is
    # thrown away with the session anyway.
    echo 'export TESTCONTAINERS_RYUK_DISABLED=true'
  } >> "$CLAUDE_ENV_FILE"
fi

# Docker for the Testcontainers tests: start the daemon and fetch postgres:17 in the background,
# so the session does not wait for it. Progress is in $log.
if command -v dockerd >/dev/null && ! docker info >/dev/null 2>&1; then
  nohup bash -c '
    dockerd >/tmp/dockerd.log 2>&1 &
    for _ in $(seq 60); do docker info >/dev/null 2>&1 && break; sleep 1; done
    if ! docker image inspect postgres:17 >/dev/null 2>&1; then
      docker pull mirror.gcr.io/library/postgres:17 && docker tag mirror.gcr.io/library/postgres:17 postgres:17
    fi
    echo "docker ready"
  ' >>"$log" 2>&1 &
fi

# Restore packages (cached in ~/.nuget/packages) so the first build is quicker.
dotnet restore --verbosity quiet >>"$log" 2>&1 || echo "session-start: dotnet restore failed, see $log" >&2
