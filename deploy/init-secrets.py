"""Creates random local secrets in deploy/secrets/ (git-ignored). Existing files are kept.

Run from anywhere: python deploy/init-secrets.py
Production secrets are not made here: they come from the operations repository (design 6.5).
"""
import base64
import os
import secrets
from pathlib import Path

SECRETS = Path(__file__).resolve().parent / "secrets"

# name -> number of random bytes (base64-encoded into the file); 0 means an empty file.
FILES = {
    "postgres_password": 24,
    "db_migrator_password": 24,
    "db_app_password": 24,
    "db_worker_password": 24,
    "db_readonly_password": 24,
    "ip_hash_key": 24,
    "data_protection_key": 32,  # wraps the data-protection key ring; must be exactly 32 bytes
    "smtp_password": 0,  # empty locally: Mailpit needs no password
    "ai_api_key": 0,  # empty: the AI draft (design 8.11.4) stays off until a Claude API key is put here and LT2_AI_PROVIDER=claude
}


def main() -> None:
    SECRETS.mkdir(parents=True, exist_ok=True)
    for name, size in FILES.items():
        path = SECRETS / name
        if path.exists() and (path.stat().st_size > 0 or size == 0):
            continue
        value = base64.b64encode(secrets.token_bytes(size)).decode("ascii") if size else ""
        path.write_text(value, encoding="ascii", newline="")
        if os.name != "nt":
            path.chmod(0o600)
        print(f"created {name}")
    print(f"secrets in {SECRETS}")


if __name__ == "__main__":
    main()
