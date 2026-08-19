# Security

This repository is public. Treat every commit as world-readable.

## Never commit

- Railway account, workspace, or project tokens
- `.env` files, user-secrets, or `appsettings.*.local.json`
- Private keys, certificates, or NuGet API keys
- Real project/environment IDs in samples if they are not meant to be public

Use `.env.example` and `Parameters__*` / environment variables on the machine that runs `aspire deploy`. GitHub secret scanning and push protection are enabled. PRs also run Gitleaks.

## Reporting a vulnerability

If you find a secret or security issue in this repo, do not open a public issue. Report it privately through [GitHub security advisories](https://github.com/intrepid-developer/aspire-hosting-railway/security/advisories/new) or contact Chris via [intrepid-developer.com](https://intrepid-developer.com). Rotate the leaked credential immediately.
