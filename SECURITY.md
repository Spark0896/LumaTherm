# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected security vulnerability. Use the repository's [private GitHub security advisory form](https://github.com/Spark0896/LumaTherm/security/advisories/new) with a concise reproduction, affected version, impact, and any safe mitigation. Do not include credentials, certificates, user logs, or hardware identifiers unless essential to the report.

If GitHub private reporting is unavailable, do not disclose vulnerability details publicly. Open only a minimal public issue requesting that private reporting be enabled or a private contact route be provided; do not include a reproduction, affected version, impact, logs, credentials, certificates, or hardware identifiers in that issue.

Reports are triaged on a best-effort basis. Please allow time for acknowledgement and coordinated remediation before disclosure.

## Release trust

Only obtain binaries from the project's GitHub Releases page and verify their SHA-256 values against the adjacent `SHA256SUMS.txt`. The portable flow may import a public certificate into `LocalMachine\TrustedPeople`; it never needs a private key. Treat an unexpected certificate prompt, mismatched signer, checksum mismatch, or changed artifact name as a reason to stop.
