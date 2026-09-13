# Changelog

All notable changes are documented here. Version numbers follow semantic versioning.

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
