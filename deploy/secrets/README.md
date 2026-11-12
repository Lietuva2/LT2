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

Production values live in the operations repository, encrypted with SOPS and age, and are mounted at deploy time
(design 6.5). Never commit a real value.
