"""Creates random secrets for deploy/compose.yaml. Existing files are kept.

Run from anywhere: python deploy/init-secrets.py [DIRECTORY]
Without DIRECTORY the secrets go to deploy/secrets/ (git-ignored), where compose.yaml looks by default. A server keeps
them outside the application tree instead and points LT2_SECRETS_DIR there (README.md, "Behind a server's existing
Caddy"). Production secrets are not made here: they come from the operations repository (design 6.5).

Compose mounts each file into the containers as it is on the host, owner and mode included, and no container reads
them as root. So on Linux, run as root, each file is given to the user of the images that read it and to nobody else
(0400, or 0440 for the one file two images share), and the directory is open to root only. Run as another user on
Linux, files cannot be given away: they are left readable (0644) inside a directory only that user can open. Run it
again after replacing a file (for example putting an API key into ai_api_key): it sets the owners and modes again.
On Windows (Docker Desktop) none of this applies.
"""
import base64
import os
import secrets
import sys
from pathlib import Path

DEFAULT = Path(__file__).resolve().parent / "secrets"

# The users the images run as: "app" in the .NET images (web, worker, migrator), "postgres" in postgres:17.
APP = 1654
POSTGRES = 999

# name -> number of random bytes (base64-encoded into the file); 0 means an empty file.
FILES = {
    "postgres_password": 24,
    "db_migrator_password": 24,
    "db_app_password": 24,
    "db_worker_password": 24,
    "db_readonly_password": 24,
    "ip_hash_key": 24,
    "mail_hash_key": 32,  # keys the suppression list's hashes, the MPs' preference links and the members' stop links (design 8.19.4, 8.19.6, 8.24.1)
    "device_hash_key": 32,  # keys the known browsers' hashes (design 8.20.2)
    "data_protection_key": 32,  # wraps the data-protection key ring; must be exactly 32 bytes
    "smtp_password": 0,  # empty locally: Mailpit needs no password
    "notices_smtp_password": 0,  # the notices stream's own SMTP account (design 8.19.3); empty: the common account is used
    "mp_smtp_password": 0,  # MP mail's own SMTP account (design 8.19.3); empty: the common account is used
    "ai_api_key": 0,  # empty: the AI draft (design 8.11.4) stays off until a Claude API key is put here and LT2_AI_PROVIDER=claude
}

# name -> (owner, group, mode) when run as root on Linux. postgres_password is read by postgres and by the migrator.
OWNERS = {name: (APP, APP, 0o400) for name in FILES}
OWNERS["postgres_password"] = (POSTGRES, APP, 0o440)


def main() -> None:
    target = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else DEFAULT
    posix = os.name != "nt"
    root = posix and os.geteuid() == 0
    target.mkdir(parents=True, exist_ok=True)
    if posix:
        target.chmod(0o700)
    for name, size in FILES.items():
        path = target / name
        if not (path.exists() and (path.stat().st_size > 0 or size == 0)):
            value = base64.b64encode(secrets.token_bytes(size)).decode("ascii") if size else ""
            path.write_text(value, encoding="ascii", newline="")
            print(f"created {name}")
        if root:
            owner, group, mode = OWNERS[name]
            os.chown(path, owner, group)
            path.chmod(mode)
        elif posix:
            path.chmod(0o644)
    print(f"secrets in {target}")


if __name__ == "__main__":
    main()
