# Development

## Prerequisites

Use Windows 11, the .NET SDK pinned in `global.json` (8.0.423), and a Windows SDK compatible with the packaging project. A developer machine may use NVIDIA tooling or MSI Afterburner for read-only diagnostics, but they are not prerequisites for a clean build.

```powershell
dotnet restore LumaTherm.sln -p:NuGetAudit=false
dotnet build LumaTherm.sln -c Release --no-restore -p:NuGetAudit=false
dotnet test LumaTherm.sln -c Release --no-build -p:NuGetAudit=false
```

The repository can also use its local SDK bootstrap when present:

```powershell
& .\.dotnet\dotnet.exe test .\tests\LumaTherm.Packaging.Tests -c Release -p:NuGetAudit=false
```

## Test-first workflow

Write a focused test that fails for the required behavior, run it and record the expected failure, then make the smallest change that passes it. Run the owning test project after each change and the full solution before review. Hardware commands are not a substitute for automated tests and must remain separate from CI.

## Boundaries and persisted data

Keep policy and domain logic in Core; place Windows integration behind Infrastructure interfaces; compose concrete implementations in App. Preserve settings migration rules described in [architecture](architecture.md#settings-and-recovery). New persisted settings require a default, validation, migration behavior, recovery consideration, and tests.

For localized UI, add every key to both `Strings.en-US.xaml` and `Strings.ru-RU.xaml`, keep formatting tokens identical, and test resource parity. User-facing release links must remain under `https://github.com/Spark0896/LumaTherm`.

## Safe diagnostics

These smoke commands are read-only:

```powershell
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- sensor --json
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- sensor --skip-nvml --json
& .\.dotnet\dotnet.exe run --project tools\LumaTherm.Smoke -c Release -- lights --json
```

`cycle` and `simulate` can affect physical lighting only after `--confirm-light-write` and an interactive `YES`. They are manual, confirmation-gated checks—not CI inputs.

## Packaging development

`scripts/build-release.ps1 -Mode Plan` describes a build without mutating package stores. A full release requires an external PFX and produces only the setup executable, portable zip, and `SHA256SUMS.txt` in `dist`. Never add certificates or release output to source control. See [releasing](releasing.md) for certificate handling, manual gates, and rollback.
