# Releasing

## Scope and prerequisites

Release work is a manual, Windows-only operation. It is separate from CI and must not run in response to a pull request. Before starting, ensure all tests pass, set the intended version consistently, and ensure source documentation separates implemented behavior from recorded hardware evidence. The currently configured artifact names below are for `1.2.1`; unpublished changes need a new version before publication rather than replacement of existing release assets. Install the .NET SDK pinned by `global.json` and restore the solution to obtain the Windows SDK BuildTools dependency. The release script prefers `.dotnet\dotnet.exe` when present and otherwise uses the installed `dotnet.exe` on `PATH`; it runs SDK commands from the repository root so `global.json` still selects the pinned SDK. Inno Setup 6.7.3 is the compiler used by the compile-only validation gate.

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

- `LumaTherm-1.2.1-win-x64-setup.exe`
- `LumaTherm-1.2.1-portable-win-x64.zip`
- `SHA256SUMS.txt`

An official portable Inno Setup compiler can be selected with `-InnoSetupPath
'D:\tools\Inno\ISCC.exe'` in both Plan and Full modes. This does not enable the
test-only repository, SDK, transaction, or registration overrides. Verify the
official distribution's SHA-256 before extracting or executing it.

Inspect the artifacts and verify each SHA-256 before publishing. The portable archive itself contains a sparse identity, public `.cer`, signed payload anchor, internal checksum manifest, registration helpers, `README.md`, and `LICENSE`. Do not alter its contents after signing/building.

## Manual gates

1. Confirm certificate ownership and the exact PFX path before a full build.
2. Run setup/portable registration only with explicit authorization. Certificate trust and sparse registration mutate system state and are intentionally not CI operations. Follow the [same-account setup requirements](installation.md#install): launch normally from the intended administrator account with UAC enabled. Silent setup still requires a qualifying unelevated original token; `/ALLOWCERTIMPORT` is certificate consent only. Portable registration must use the same intended account's elevated PowerShell.
3. Before any physical lighting write, explain that Windows Dynamic Lighting ownership will be temporarily taken and later released; obtain an explicit confirmation. Run manual hardware acceptance only after that confirmation.
4. Record actual results, timestamps, hashes, and screenshots only after observing them. Do not substitute source tests for hardware acceptance or claim a GitHub publication before it happens.
5. Create a non-draft, non-prerelease GitHub Release with tag `v<version>` matching the finalized source version and commit, attach exactly the public artifacts, and then verify the release page and update feed behavior. Use a new version for changes to a published installer or portable package.

## Rollback

If packaging fails before promotion, the script restores the prior `dist` transaction where possible. If setup or registration fails, stop and inspect the error; the registration helper rolls back a newly registered identity and newly imported certificate when it owns them. A same-version repair must first remove the previous exact sparse identity; if replacement fails, that previous identity cannot be restored automatically, and the error explicitly reports its absence. Retry registration from the verified payload after resolving the cause. For an installed version, use the uninstaller to remove the exact LumaTherm identity, retaining user data unless removal is explicitly wanted. For portable, unregister from its original external folder before replacing it.

Never delete unknown package identities, certificates, or user directories as a rollback shortcut.
