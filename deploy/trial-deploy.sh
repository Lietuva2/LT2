#!/usr/bin/env bash
# Lietuva 2.0: the trial server's deploy command (design 9.2, "Delivery"; README.md, "Deploying from GitHub").
#
# Installed by root as /usr/local/sbin/lt2-deploy and run only as the forced command of the deploy key in
# root's authorized_keys, so whoever holds that key (the "Deploy to trial" workflow) can run nothing else:
#
#   command="/usr/local/sbin/lt2-deploy",restrict ssh-ed25519 AAAA... lt2-deploy-github
#
# The client's words arrive in SSH_ORIGINAL_COMMAND (or as arguments when root runs it by hand):
#
#   deploy <commit>   read a tar from standard input holding lt2-app.tar.gz (git archive of the commit) and
#                     lt2-images.tar.gz (docker save of lt2/web, worker, migrator and seimas-standin tagged with it),
#                     then the README's steps 2 and 3: load, swap the app, migrate, restart, check health
#   rollback          put back app.previous and its image tag, and restart (the database stays as migrated)
#   status            print the commit that runs
#
# A new copy of this file in a deployed commit changes nothing: only root installs it, by hand.
set -euo pipefail
umask 077

LT2_HOME="${LT2_HOME:-/opt/lt2}"
ENV_FILE="$LT2_HOME/lt2.env"
APP="$LT2_HOME/app"
IMAGES=(web worker migrator seimas-standin)
# The services the README's step 3 restarts; those whose profile the server does not run are left out.
SERVICES=(web worker mailpit seimas-standin)

log() { printf '%s %s\n' "$(date -u +%H:%M:%S)" "$*"; }
fail() { log "FAILED: $*" >&2; exit 1; }

compose() {
  (cd "$APP" && docker compose --env-file "$ENV_FILE" -f deploy/compose.yaml -f deploy/compose.shared-proxy.yaml "$@")
}

env_value() { sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1; }

# sed -i would give lt2.env a new inode and the default mode; this keeps both.
set_tag() {
  local tag="$1" tmp
  tmp="$(mktemp)"
  sed "s/^LT2_IMAGE_TAG=.*/LT2_IMAGE_TAG=$tag/" "$ENV_FILE" > "$tmp"
  grep -qx "LT2_IMAGE_TAG=$tag" "$tmp" || { rm -f "$tmp"; fail "lt2.env has no LT2_IMAGE_TAG line"; }
  cat "$tmp" > "$ENV_FILE"
  rm -f "$tmp"
}

restart() {
  local active run=() s
  active="$(compose config --services)"
  for s in "${SERVICES[@]}"; do
    grep -qx "$s" <<<"$active" && run+=("$s")
  done
  compose up -d --no-build --no-deps "${run[@]}"
}

check_health() {
  local site _
  site="$(env_value LT2_SITE_ADDRESS)"
  [[ -n "$site" ]] || fail "lt2.env has no LT2_SITE_ADDRESS"
  for _ in $(seq 1 45); do
    if curl -fsS -o /dev/null --max-time 5 "https://$site/health/ready"; then
      log "https://$site/health/ready answers 200"
      return 0
    fi
    sleep 2
  done
  return 1
}

# Keeps the images of the commit that runs and of the one before it (for a rollback); removes older lt2 images.
prune_images() {
  local keep_a="$1" keep_b="$2" name ref tag
  for name in "${IMAGES[@]}"; do
    while read -r ref; do
      tag="${ref##*:}"
      [[ "$tag" == "$keep_a" || "$tag" == "$keep_b" || "$tag" == "<none>" ]] && continue
      docker image rm "$ref" >/dev/null 2>&1 && log "removed $ref" || true
    done < <(docker image ls --format '{{.Repository}}:{{.Tag}}' "lt2/$name")
  done
}

WORK=""
deploy() {
  local commit="$1" previous
  [[ "$commit" =~ ^[0-9a-f]{40}$ ]] || fail "the commit must be a full 40-character hash"
  [[ -f "$ENV_FILE" && -d "$APP" ]] || fail "$LT2_HOME is not set up (README.md)"
  previous="$(cat "$LT2_HOME/deployed-commit" 2>/dev/null || true)"

  WORK="$LT2_HOME/incoming/github-$commit"
  rm -rf "$WORK"
  mkdir -p "$WORK/app"
  trap 'rm -rf "$WORK"' EXIT

  log "receiving $commit"
  # Only the two named files are taken from the stream, owned by root whatever the archive says.
  tar -x -f - -C "$WORK" --no-same-owner --no-same-permissions lt2-app.tar.gz lt2-images.tar.gz
  tar -x -z -f "$WORK/lt2-app.tar.gz" -C "$WORK/app" --no-same-owner
  [[ -f "$WORK/app/deploy/compose.yaml" ]] || fail "the app archive has no deploy/compose.yaml"

  log "loading images"
  docker load -i "$WORK/lt2-images.tar.gz"
  for name in "${IMAGES[@]}"; do
    docker image inspect "lt2/$name:$commit" >/dev/null || fail "the images hold no lt2/$name:$commit"
  done

  log "swapping the app (the old one stays in app.previous)"
  cp -p "$ENV_FILE" "$LT2_HOME/lt2.env.previous"
  rm -rf "$LT2_HOME/app.previous"
  mv "$APP" "$LT2_HOME/app.previous"
  mv "$WORK/app" "$APP"
  set_tag "$commit"

  log "running the migrator"
  if ! compose run --rm --no-deps migrator; then
    log "the migrator failed: putting the previous app back"
    rm -rf "$APP"
    mv "$LT2_HOME/app.previous" "$APP"
    cat "$LT2_HOME/lt2.env.previous" > "$ENV_FILE"
    fail "migrator; nothing was restarted, the previous version still runs"
  fi

  log "restarting"
  restart
  printf '%s\n' "$commit" > "$LT2_HOME/deployed-commit"
  check_health || fail "/health/ready did not answer 200 within 90 s (to go back: run the workflow with "rollback")"

  prune_images "$commit" "$previous"
  docker image prune -f >/dev/null
  log "deployed $commit"
}

rollback() {
  [[ -d "$LT2_HOME/app.previous" && -f "$LT2_HOME/lt2.env.previous" ]] || fail "there is no previous version"
  local current back
  current="$(cat "$LT2_HOME/deployed-commit" 2>/dev/null || true)"
  back="$(sed -n 's/^LT2_IMAGE_TAG=//p' "$LT2_HOME/lt2.env.previous" | tail -n 1)"
  [[ "$back" =~ ^[0-9a-f]{40}$ ]] || fail "lt2.env.previous names no commit"
  log "rolling back from $current to $back"
  rm -rf "$LT2_HOME/app.failed"
  mv "$APP" "$LT2_HOME/app.failed"
  mv "$LT2_HOME/app.previous" "$APP"
  mv "$LT2_HOME/app.failed" "$LT2_HOME/app.previous"
  cp -p "$ENV_FILE" "$LT2_HOME/lt2.env.failed"
  set_tag "$back"
  mv "$LT2_HOME/lt2.env.failed" "$LT2_HOME/lt2.env.previous"
  restart
  printf '%s\n' "$back" > "$LT2_HOME/deployed-commit"
  check_health || fail "/health/ready did not answer 200 within 90 s"
  log "running $back"
}

main() {
  local words
  if [[ -n "${SSH_ORIGINAL_COMMAND:-}" ]]; then
    read -r -a words <<<"$SSH_ORIGINAL_COMMAND"
  else
    words=("$@")
  fi
  # One deploy at a time; a second one waits up to ten minutes.
  exec 9>/run/lock/lt2-deploy
  flock -w 600 9 || fail "another deploy is running"
  case "${words[0]:-} ${#words[@]}" in
    "deploy 2") deploy "${words[1]}" ;;
    "rollback 1") rollback ;;
    "status 1") cat "$LT2_HOME/deployed-commit" ;;
    *) fail "usage: deploy <commit> | rollback | status" ;;
  esac
}

main "$@"
