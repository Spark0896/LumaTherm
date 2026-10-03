# Releasing

## Scope and prerequisites

Release work is a manual, Windows-only operation. It is separate from CI and must not run in response to a pull request. Before starting, ensure all tests pass, the intended version is `1.2.0`, and source documentation accurately separates implemented behavior from recorded hardware evidence.

Keep the PFX outside the repository and outside `dist`; `scripts/build-release.ps1` rejects repository-local or `dist` certificates. Use a stable, access-controlled path. The current script accepts `-CertificatePassword` as a PowerShell parameter, which can expose a password to process-argument inspection. Mitigate operationally: run from a private elevated session, do not paste the command into shared logs/history, restrict local access, and rotate the certificate/password if exposure is suspected. Never put the PFX, password, or signing data into CI, GitHub secrets for this workflow, issue text, or source control.

## Build and verify

From the repository root, use the planned command first:

```powershell
.\scripts\build-release.ps1 -Mode Plan
```

When authorization and signing material are available, the manual full build is:

```powershell
.\scripts\build-release.ps1 -Mode Full -CertificatePath 'D:\secure\LumaTherm.pfx' -CertificatePassword '<secure-password>'
```

The public output contract is exactly:

- `LumaTherm-1.2.0-win-x64-setup.exe`
- `LumaTherm-1.2.0-portable-win-x64.zip`
- `SHA256SUMS.txt`

An official portable Inno Setup compiler can be selected with `-InnoSetupPath
'D:\tools\Inno\ISCC.exe'` in both Plan and Full modes. This does not enable the
test-only repository, SDK, transaction, or registration overrides. Verify the
official distribution's SHA-256 before extracting or executing it.

Inspect the artifacts and verify each SHA-256 before publishing. The portable archive itself contains a sparse identity, public `.cer`, signed payload anchor, internal checksum manifest, registration helpers, `README.md`, and `LICENSE`. Do not alter its contents after signing/building.

## Manual gates

1. Confirm certificate ownership and the exact PFX path before a full build.
2. Run setup/portable registration only with explicit authorization. Certificate trust and sparse registration mutate system state and are intentionally not CI operations.
3. Before any physical lighting write, explain that Windows Dynamic Lighting ownership will be temporarily taken and later released; obtain an explicit confirmation. Run manual hardware acceptance only after that confirmation.
4. Record actual results, timestamps, hashes, and screenshots only after observing them. Do not substitute source tests for hardware acceptance or claim a GitHub publication before it happens.
5. Create a non-draft, non-prerelease GitHub Release with semver tag `1.2.0`, attach exactly the public artifacts, and then verify the release page and update feed behavior.

## Rollback

If packaging fails before promotion, the script restores the prior `dist` transaction where possible. If setup or registration fails, stop and inspect the error; the registration helper rolls back its own identity/certificate work when it owns it. For an installed version, use the uninstaller to remove the exact LumaTherm identity, retaining user data unless removal is explicitly wanted. For portable, unregister from its original external folder before replacing it.

Never delete unknown package identities, certificates, or user directories as a rollback shortcut.
