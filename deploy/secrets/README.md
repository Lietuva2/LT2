# deploy/secrets

Local secret files for `deploy/compose.yaml`, one value per file. Created by
`python deploy/init-secrets.py`; everything here except this README is git-ignored.

| File | Used by | Meaning |
|---|---|---|
| `postgres_password` | postgres, migrator | Superuser password; used only by the migrator to create roles. |
| `db_migrator_password` | migrator | `lt2_migrator`: owns the schemas, runs migrations. |
| `db_app_password` | migrator, web | `lt2_app`: data access only. |
| `db_worker_password` | migrator, worker | `lt2_worker`: data access plus the `hangfire` schema. |
| `db_readonly_password` | migrator | `lt2_readonly`: analysis, no access to `identity`. |
| `data_protection_key` | web | Base64 of 32 bytes; wraps the data-protection keys stored in PostgreSQL. |
| `ip_hash_key` | web | Key for hashing client IP addresses in the audit log. |
| `smtp_password` | worker | SMTP password; empty locally (Mailpit). |
| `mail_hash_key` | web, worker | Key of MP mail (design 8.19): the suppression list's keyed hashes, the MPs' link tokens and the members' stop links (design 8.24.1). Changing it breaks every link already emailed and forgets the suppressed addresses. |
| `device_hash_key` | web | Key of the known browsers' hashes (design 8.20.2). Changing it makes every browser a stranger to its accounts again (sign-in still works). |
| `mp_smtp_password` | worker | Password of MP mail's own SMTP account (`LT2_MP_SMTP_*`); empty while MP mail uses the common account. |
| `ai_api_key` | web, worker | Claude API key; empty keeps the AI features off. |
| `mailpit_ui_auth` | mailpit | Server only, with `compose.shared-proxy.yaml`: the viewer's name and bcrypt hash (README.md, "The caught emails"). Not made by `init-secrets.py`; not used locally. |

Production values live in the operations repository, encrypted with SOPS and age, and are mounted at deploy time
(design 6.5). Never commit a real value.
