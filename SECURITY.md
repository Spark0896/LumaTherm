# Security policy

## Reporting a vulnerability

Do not open a public issue for a suspected security vulnerability. Contact [Spark0896](https://github.com/Spark0896) privately through GitHub with a concise reproduction, affected version, impact, and any safe mitigation. Do not include credentials, certificates, user logs, or hardware identifiers unless essential and explicitly requested through a private channel.

Reports are triaged on a best-effort basis. Please allow time for acknowledgement and coordinated remediation before disclosure.

## Release trust

Only obtain binaries from the project's GitHub Releases page and verify their SHA-256 values against the adjacent `SHA256SUMS.txt`. The portable flow may import a public certificate into `LocalMachine\TrustedPeople`; it never needs a private key. Treat an unexpected certificate prompt, mismatched signer, checksum mismatch, or changed artifact name as a reason to stop.
