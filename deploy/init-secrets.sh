#!/usr/bin/env sh
# Creates random local secrets in deploy/secrets/ (git-ignored). Existing files are kept.
# Production secrets are not made here: they come from the operations repository (design 6.5).
set -eu
dir="$(cd "$(dirname "$0")" && pwd)/secrets"
mkdir -p "$dir"

make() {
  if [ ! -s "$dir/$1" ] && [ "$1" != "smtp_password" ]; then
    head -c "$2" /dev/urandom | base64 | tr -d '\n' > "$dir/$1"
    echo "created $1"
  fi
}

for name in postgres_password db_migrator_password db_app_password db_worker_password db_readonly_password ip_hash_key; do
  make "$name" 24
done
make data_protection_key 32
[ -f "$dir/smtp_password" ] || : > "$dir/smtp_password"
chmod 600 "$dir"/* 2>/dev/null || true
echo "secrets in $dir"
