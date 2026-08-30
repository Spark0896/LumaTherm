# Contributing to LumaTherm

Thanks for helping. By contributing, you agree that your work may be distributed under the [MIT License](LICENSE).

## Before opening a change

1. Discuss substantial behavior or hardware-safety changes in an issue first.
2. Keep the Core project platform-neutral; Windows APIs belong in Infrastructure or App composition.
3. Add a focused failing test before changing behavior, then make it pass.
4. Do not add vendor DLL, raw HID, signing material, device identifiers, screenshots presented as evidence, or generated release output.

## Local checks

Use the SDK selected by `global.json` and run:

```powershell
dotnet restore LumaTherm.sln -p:NuGetAudit=false
dotnet build LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test LumaTherm.sln -c Release --no-build -p:NuGetAudit=false
git diff --check
```

Use `docs/development.md` for project boundaries, settings migrations, localization, and packaging-test commands. Hardware writes and registration are confirmation-gated manual work; do not put them in automated tests or CI.
