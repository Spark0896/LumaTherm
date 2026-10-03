# Changelog

All notable changes are documented here. Version numbers follow semantic versioning.

## [1.2.0] - 2026-10-03

### Changed

- Arctic Minimal WPF interface, two-peak identity, responsive navigation, fixed
  settings/test actions, dark color picker, and consistent EN/RU controls.
- Factory profile: 35°C blue `#006BFF`, 65°C green `#3CFF00`, 85°C red `#FF0000`.
  Custom profiles remain editable; Default colors resets only the palette.
- All previews use the physical eased HSV map. Dashboard summaries handle
  arbitrary point counts; temperature labels avoid overlap.
- English and Russian READMEs with matching actual application screenshots.

### Fixed

- Tray menu survives sensor updates; minimize/restore/Exit work independently.
  Shutdown releases services before WPF exits.
- Startup applies immediately, reflects Windows state and explains user/policy
  refusal with access to Windows startup settings.
- Sorted point insertion prevents Add-point crashes; Delete retains focus/selection.
- Invalid numeric text blocks Save/Test/Apply. Test failure retains a retryable draft;
  Cancel and close release the temporary session and restore normal operation.
- Black/gray transitions preserve the neighboring hue; primary-button text renders
  with the intended contrast.

## [1.1.0] - 2026-09-13

### Added

- Self-contained x64 setup and portable release contracts with SHA-256 manifests.
- Sparse Windows lighting identity registration with explicit public-certificate trust.
- English and Russian documentation, contributor guidance, and Windows-only CI.
- Editable multi-stop temperature gradient, interactive lighting test window,
  configurable tray menu, and English/Russian UI selection.
- Opt-in Windows startup task for the installed app; autostart remains disabled
  by default and can be changed in Settings.
- Higher-contrast interface palette, consistent action buttons, vector window
  controls, and a dark language selector.

### Changed

- The default hot stop is now pure red (`85°C → #FF0000`).
- Per-segment HSV interpolation now eases smoothly into every control point,
  reducing visible single-degree jumps on physical LEDs.
- The lighting-test action now lives with the color-scale editor.

### Fixed

- Installer registration now places the explicitly approved public certificate in the Windows machine trusted-root store required by AppX, and the uninstaller removes that exact certificate.
- Autostart preserves and resumes the last saved thermal-mode state instead of
  treating a Windows restart as a request to disable it.
- Tray-menu preview actions remain readable against the dark preview surface.

### Notes

- The release is designed for Windows Dynamic Lighting devices exposed through
  LampArray. It does not claim universal vendor-RGB compatibility.
- The setup and portable assets are signed with a temporary local publisher
  certificate. Setup imports its public certificate only when the user
  explicitly approves it; the private key is never imported.
