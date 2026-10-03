# LumaTherm product

LumaTherm is an open-source WPF desktop utility for x64 Windows 11 22H2+ that maps live GPU temperature to devices exposed through Windows Dynamic Lighting LampArray. NVIDIA NVML is the primary sensor; MSI Afterburner shared memory is an optional fallback. Settings and logs are local. Updates are manually checked on GitHub.

The primary workflow is: choose an available device, set a thermal profile, preview it, enable thermal sync, and leave monitoring running in the tray. The audience wants quick, understandable temperature feedback while gaming or working, without needing vendor-specific RGB integrations.

Confirmed redesign requirements: follow the user's Arctic Minimal reference; full navigation labels; calm dark blue-gray surfaces; clear temperature and hardware state; usable color editing; stable tray interaction; truthful Windows startup state; English and Russian UI and documentation. The user explicitly selected factory colors 35°C #006BFF, 65°C #3CFF00, 85°C #FF0000. Preserve custom saved profiles.

## Direction contract

THESIS: A calm thermal control panel with a clear temperature-to-light relationship and explicit actions for editing, testing, and saving.

OWN-WORLD: Dark blue-gray canvas and rail, bordered rounded panels, ice-blue navigation and controls, Segoe UI, the two-peak Arctic logo. Temperature colors belong to data and profiles.

STORY: Understand the current GPU reading and device availability; preview a profile; choose whether lighting follows GPU temperature; keep control available in the tray.

FIRST VIEWPORT: A 178 DIP labeled sidebar beside two columns of cards. A 52 DIP GPU temperature and ring lead; the 60-second chart sits alongside. Device and sync controls and the profile occupy the second row. Settings keep Save and Reset in a fixed footer; lighting test uses a large temperature slider and uniform schematic LED preview.

FORM: Implement the user's selected Arctic Minimal reference in the existing native WPF application; no alternative selection round is needed. The 820 DIP content breakpoint stacks panels and preserves scroll access. Signature interaction: selecting and dragging temperature points updates a sampled HSV gradient using the same engine as physical lighting.

FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
