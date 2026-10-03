---
name: LumaTherm Arctic Minimal
description: A calm native Windows thermal control panel with explicit temperature-to-light feedback.
colors:
  ice-accent: "#47B4FF"
  window-background: "#131D25"
  rail-background: "#14212B"
  panel-background: "#202D37"
  panel-secondary: "#182630"
  primary-text: "#F4F7FA"
  secondary-text: "#D5DDE3"
  muted-text: "#AEBAC4"
  subtle-line: "#394B59"
  icon-idle: "#B0BBC4"
  action-background: "#2A3B49"
  action-border: "#4A6172"
  primary-action-foreground: "#071A29"
  navigation-active: "#294459"
  navigation-hover: "#273F51"
  editor-border: "#53697A"
  selection-background: "#2B4B63"
  switch-off: "#50606D"
  switch-thumb: "#EEF6FD"
  validation-border: "#FF5D65"
  validation-text: "#FFADB2"
  success: "#61DDA6"
  status-active-background: "#23493D"
  status-active-text: "#98EBC6"
  status-idle-background: "#334351"
  brand-wordmark: "#B5E2FF"
  logo-light: "#ABE3FF"
  logo-blue: "#389AF3"
  marker-background: "#171B20"
  marker-stroke: "#F1F8FB"
  factory-cold: "#006BFF"
  factory-warm: "#3CFF00"
  factory-hot: "#FF0000"
typography:
  display:
    fontFamily: "Segoe UI"
    fontSize: "52 DIP"
    fontWeight: 600
  test-display:
    fontFamily: "Segoe UI"
    fontSize: "48 DIP"
    fontWeight: 600
  headline:
    fontFamily: "Segoe UI"
    fontSize: "28 DIP"
    fontWeight: 600
  dialog-headline:
    fontFamily: "Segoe UI"
    fontSize: "24 DIP"
    fontWeight: 600
  title:
    fontFamily: "Segoe UI"
    fontSize: "15 DIP"
    fontWeight: 600
  panel-title:
    fontFamily: "Segoe UI"
    fontSize: "14 DIP"
    fontWeight: 600
  body:
    fontFamily: "Segoe UI"
    fontSize: "13 DIP"
    fontWeight: 400
  action-label:
    fontFamily: "Segoe UI"
    fontSize: "13 DIP"
    fontWeight: 600
  label:
    fontFamily: "Segoe UI"
    fontSize: "12 DIP"
    fontWeight: 400
  chart-caption:
    fontFamily: "Segoe UI"
    fontSize: "11 DIP"
    fontWeight: 400
  chart-axis:
    fontFamily: "Segoe UI"
    fontSize: "10 DIP"
    fontWeight: 400
  wordmark:
    fontFamily: "Segoe UI"
    fontSize: "16 DIP"
    fontWeight: 600
  ring-value:
    fontFamily: "Segoe UI Variable"
    fontSize: "26 DIP"
    fontWeight: 600
  ring-label:
    fontFamily: "Segoe UI Variable"
    fontSize: "12 DIP"
    fontWeight: 400
rounded:
  item: "4 DIP"
  control: "6 DIP"
  inset: "8 DIP"
  panel: "10 DIP"
  switch: "12 DIP"
spacing:
  step-4: "4 DIP"
  step-6: "6 DIP"
  step-8: "8 DIP"
  step-10: "10 DIP"
  step-12: "12 DIP"
  step-14: "14 DIP"
  step-16: "16 DIP"
  step-18: "18 DIP"
  step-20: "20 DIP"
  step-22: "22 DIP"
  step-24: "24 DIP"
  step-26: "26 DIP"
  step-28: "28 DIP"
components:
  button-primary:
    backgroundColor: "{colors.ice-accent}"
    textColor: "{colors.primary-action-foreground}"
    typography: "{typography.action-label}"
    rounded: "{rounded.control}"
    padding: "16,8 DIP"
  button-secondary:
    backgroundColor: "{colors.action-background}"
    textColor: "{colors.primary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "16,8 DIP"
  navigation:
    backgroundColor: "transparent"
    textColor: "{colors.secondary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "16,0 DIP"
    height: "46 DIP"
  navigation-selected:
    backgroundColor: "{colors.navigation-active}"
    textColor: "{colors.ice-accent}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "16,0 DIP"
    height: "46 DIP"
  editor-field:
    backgroundColor: "{colors.panel-secondary}"
    textColor: "{colors.primary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "10,7 DIP"
  dark-combobox:
    backgroundColor: "{colors.panel-secondary}"
    textColor: "{colors.primary-text}"
    typography: "{typography.body}"
    rounded: "{rounded.control}"
    padding: "10,5 DIP"
  panel:
    backgroundColor: "{colors.panel-background}"
    textColor: "{colors.primary-text}"
    rounded: "{rounded.panel}"
  toggle-off:
    backgroundColor: "{colors.switch-off}"
    rounded: "{rounded.switch}"
    width: "44 DIP"
    height: "28 DIP"
  toggle-on:
    backgroundColor: "{colors.ice-accent}"
    rounded: "{rounded.switch}"
    width: "44 DIP"
    height: "28 DIP"
  temperature-slider:
    backgroundColor: "{colors.switch-off}"
    height: "42 DIP"
  thermal-point:
    backgroundColor: "transparent"
    width: "28 DIP"
    height: "34 DIP"
---

# Design System: LumaTherm

## Overview

**Creative North Star: "Arctic Minimal"**

A calm dark blue-gray control panel follows the user's selected Arctic Minimal reference. Ice-blue controls, fully labeled navigation, bordered rounded panels, and the two-peak Arctic logo establish the visual identity. The temperature reading leads the hierarchy; hardware availability and explicit editing, testing, and saving actions remain easy to find.

This is an extraction of the current native WPF implementation. The frontmatter is normative for the documented tokens; application source remains the implementation authority. Every size in this document is a WPF device-independent unit (DIP), not a screenshot pixel measurement. WPF thickness shorthand follows horizontal,vertical or left,top,right,bottom order. Font weights map Normal to 400 and SemiBold to 600. No line-height, letter-spacing, shadow, transition duration, or tonal ramp is invented where the source does not define one.

**Key Characteristics:**

- Dark blue-gray canvas, rail, and tonal panels.
- Ice-blue action and selection states.
- Temperature colors reserved for profile and lighting data.
- Native keyboard, focus, validation, modal, tray, and resizing behavior.
- English and Russian resource-driven UI.

The extraction sources are [Theme.xaml](src/LumaTherm.App/Resources/Theme.xaml), [MainWindow.xaml](src/LumaTherm.App/MainWindow.xaml), their view and control files, [LogoGeometry.xaml](src/LumaTherm.App/Assets/LogoGeometry.xaml), and [ThermalProfile.cs](src/LumaTherm.Core/Colors/ThermalProfile.cs). The accompanying [.impeccable/design.json](.impeccable/design.json) contains layout, state, source mapping, and component-preview extensions that do not fit the frontmatter schema.

Current visual evidence was captured on 2026-10-03:

| Surface | English | Russian |
| --- | --- | --- |
| Dashboard | [Capture](docs/screenshots/en/dashboard.png) | [Capture](docs/screenshots/ru/dashboard.png) |
| Settings | [Capture](docs/screenshots/en/settings.png) | [Capture](docs/screenshots/ru/settings.png) |
| Lighting test | [Capture](docs/screenshots/en/lighting-test.png) | [Capture](docs/screenshots/ru/lighting-test.png) |
| Color picker | [Capture](docs/screenshots/en/color-picker.png) | [Capture](docs/screenshots/ru/color-picker.png) |
| About | [Capture](docs/screenshots/en/about.png) | [Capture](docs/screenshots/ru/about.png) |

The lighting-test captures include a scrolled viewport; they are evidence for the persistent footer and continued access to the profile editor.

## Colors

The palette uses cool neutral surfaces and an ice-blue UI accent. Saturated profile colors describe temperature and physical lighting.

### Primary

- **Ice Accent** (`colors.ice-accent`): primary actions, active navigation, keyboard focus, checked switches, slider outlines, links, and the history trace. The source resource is named `ColdColor`; that name does not make it the factory cold-point color.
- **Pale Arctic Wordmark** (`colors.brand-wordmark`): the title-bar product name. The logo uses the extracted light-to-blue gradient endpoints.

### Neutral

- **Deep Canvas** (`colors.window-background`): the workspace and dialog canvas.
- **Blue-Gray Rail** (`colors.rail-background`): sidebar, custom title bars, and fixed action footers.
- **Raised Tonal Panel** (`colors.panel-background`): grouped task areas; **Inset Panel** (`colors.panel-secondary`) supports fields, popups, and tooltips.
- **Primary, Secondary, and Muted Text**: main values and labels, supporting hardware text, then hints and captions.
- **Subtle Line**: panel outlines, workspace boundaries, and separators. **Idle Icon** supports inactive hardware and icon controls.
- **Action Surface and Border**: secondary buttons. **Editor Border** also serves popup outlines and the scroll thumb.
- **Selected and Hover Navigation**: distinct tonal fills; the selected row also carries an accent indicator.
- **Switch Off and Thumb**: gray track with a pale thumb. Checked state changes both color and thumb position.

### Semantic and temperature data

- **Success** and the active status pair: available hardware indicator and active runtime badge.
- **Validation Border**: invalid numeric field outlines. **Validation Text**: readable localized error messages in footers and the color dialog.
- **Factory Cold, Warm, and Hot**: the user-selected default profile anchors at 35°C, 65°C, and 85°C respectively. These values are defined by `ThermalProfile.Default`; existing custom saved profiles remain the user's data.

**The Two Palettes Rule.** UI accent and thermal profile colors have separate roles. Read a temperature color from the active profile and the shared color engine; do not substitute a similarly named theme brush.

**The State Plus Meaning Rule.** Color accompanies a label, accessible name, position, or shape change. Hardware availability, sync state, errors, and selected points must remain understandable without hue alone.

## Typography

The ordinary interface uses Segoe UI. The custom temperature ring currently draws its two text roles with Segoe UI Variable. Preserve this observed exception when reproducing the current implementation.

The font-size tokens record the actual role sizes; there is no inferred scale ratio. The main temperature uses `typography.display`; lighting-test temperature uses `typography.test-display`. Dashboard, settings, and test-window page headings use `typography.headline`; About and the color dialog use `typography.dialog-headline`. Settings section headings use `typography.title`, while dashboard/test panel headings use `typography.panel-title`.

Body controls and labels use `typography.body`. Supporting hints and editor instructions use `typography.label`; chart time captions and axes use the smaller chart roles. The title-bar wordmark is semi-bold in the main and test windows; the color dialog currently uses the same size with normal weight.

**The Reading First Rule.** Keep page titles, temperature values, panel headings, and supporting captions distinct. Let localized supporting copy wrap; restrict ellipsis to the intentionally bounded title-bar text and profile summary.

## Layout

The main window starts at 1180 × 800 DIP and enforces a 920 × 620 DIP minimum. Its custom title bar is 48 DIP high, with a 1 DIP outer outline and a 6 DIP native resize border. The fixed sidebar is 178 DIP wide. Navigation keeps its icon and full text at every supported width; About is anchored at the rail's bottom.

Dashboard and settings use a 28 DIP horizontal workspace inset, 26 DIP top inset, a 24 DIP heading-to-panels gap, and 16 DIP panel gaps. Dashboard columns use a 1.05:1 ratio; settings use 1.1:1. The compact threshold is measured on each content view's actual width, before its internal margins: widths below 820 DIP stack the right-hand panels below the left. It is not an outer-window or browser breakpoint.

The dashboard's effective runtime row heights are 260 DIP for the temperature card and 230 DIP for the device/sync row. Compact layout adds the chart at 240 DIP and profile at 220 DIP, each separated by 16 DIP. `ApplyLayout` owns these effective sizes; the bottom-row XAML initializer is 240 DIP and is superseded at runtime. Ordinary dashboard panels use 20 DIP padding, with 22 DIP around the leading temperature card.

Settings' content scrolls vertically while its Save and Reset footer occupies a separate grid row. The footer has 28,14 DIP padding and a top divider. Panel padding is 20 DIP, with a 16 DIP gap below each panel. Numeric temperature fields are 74 DIP wide. The About surface scrolls, keeps a 900 DIP maximum content width, and uses a 28,24,28,28 DIP inset and 24 DIP panel padding.

The lighting-test window starts at 860 × 780 DIP and enforces 700 × 580 DIP minimum. It keeps the shared 48 DIP title bar and a scrollable body with 26,24 DIP margins. Its action footer has 26,14 DIP padding and remains outside the scroll viewer. The color dialog is a fixed 470 × 550 DIP window with 24 DIP body margins.

**The Fixed Action Rule.** Save, Reset, Cancel, Apply, and their current validation feedback stay in their existing footer rows. Scrolling must not hide the action that completes the current task.

**The Content Width Rule.** Stack dashboard and settings panels at the implemented content threshold. Retain the labeled rail, vertical scrolling, and wrapped localized text.

## Elevation & Depth

The interface uses tonal layering and 1 DIP outlines. It does not define drop shadows or blur effects. The contrast between canvas, rail, panel, and inset field supplies structure; popup outlines and selected states supply separation.

The history chart's transparent fill under its line is a data-area treatment, not surface elevation. It fades the ice-blue color from alpha 45/255 to zero. The logo gradient is an identity asset; thermal gradients are data generated by the color engine.

**The Tonal Depth Rule.** Use the observed surface hierarchy and outlines to separate task areas. Preserve the flat resting state; do not introduce decorative shadow tokens.

## Shapes

Panels use the panel radius; ordinary buttons, fields, combo boxes, popups, and gradient tracks use the control radius. Inset preview containers use the inset radius, combo-box items and scrollbar thumbs use the item radius, and switches use the switch radius.

The shared keyboard focus visual has an accent outline (2 DIP), a 7 DIP corner radius, and a -3 DIP margin. Thermal-point focus uses a circular 16 DIP-radius focus border instead. The selection indicator in the rail is 2 × 26 DIP. Circular swatches and point markers retain their circular geometry rather than inheriting the panel radius.

**The Shape Indicates Role Rule.** Rounded rectangles contain tasks and actions; circles represent colors, thermal anchors, switch thumbs, and the temperature instrument.

## Components

### Buttons

The ordinary action is a compact bordered rectangle. `ActionButtonStyle` has a 100 DIP minimum width and 36 DIP minimum height; explicit compact variants reduce the minimum width. Primary actions reuse this shape with the ice accent, the dark primary-action foreground, and SemiBold weight. Their content template binds the label foreground to the button so the rendered text follows the documented token.

Hover changes the ordinary button's border to the accent. Pressed chrome opacity is 0.7. Disabled buttons use 0.45 opacity and the arrow cursor. Keyboard focus uses the shared outline. Icon buttons are 40 × 40 DIP with transparent backgrounds; window buttons are 44 × 44 DIP. Icon-only controls have resource-driven accessible names.

### Navigation

Rows are 46 DIP high with 6,3 DIP margins and 16,0 DIP padding. Hover applies the navigation hover fill; press uses 0.7 opacity. The current page has the selected fill, accent foreground, visible left indicator, and localized selected item status. Changing pages updates all of these together. Lighting test opens the owned test window rather than replacing the current page.

The main title bar supports drag and double-click maximize/restore. Its controls minimize, maximize/restore, and close. The close and minimize result follows the configured tray policy, so window disappearance must not be presented as application shutdown.

### Panels and status

Borders group temperature, history, device/sync, profile, settings, and About content. Their outline and fill come from the shared panel style. The runtime badge uses the active pair only when `RuntimeStatus.Active`; otherwise it uses the neutral status treatment and localized status text. The title-bar hardware dot follows availability, accompanied by the device name.

### Fields, selectors, and validation

Text fields have a 36 DIP minimum height. Keyboard focus changes the border to the accent and the caret is accent-colored. Numeric validation changes the border to the validation color and exposes the error through a tooltip. Temperature input accepts the documented 0–120°C domain; smoothing input uses 0.1–5 seconds. Save and Open Lighting Test are disabled while those settings fields are invalid; test Apply is disabled for an invalid selected-point temperature.

Combo boxes use the shared dark template, a 6 DIP popup radius, a 3 DIP popup offset, 4 DIP popup padding, and a 260 DIP maximum dropdown scroll height. Keyboard focus changes the outer border; disabled state uses 0.45 opacity. Highlighted and selected items share the selection fill. Device selection appears when the view model supplies a selectable device list. Language choices are System, English, and Russian.

Settings validation and test errors wrap inside their fixed footers and use an assertive accessibility live region. Update status in About uses a polite live region.

### Switches and sliders

Application switches occupy 44 × 28 DIP. The track is 24 DIP high with 3 DIP padding and an 18 DIP circular thumb. Checking moves the thumb to the right and applies the accent. Application switches use hover opacity 0.85 and disabled opacity 0.4. The dashboard's authoritative sync toggle shares the geometry but derives checked state from the runtime; its disabled opacity is 0.45.

The shared slider occupies 42 DIP height, with an 8 DIP track, 12 DIP horizontal inset, and 22 DIP thumb face inside a 24 × 28 DIP hit area. The thumb has a 2 DIP accent outline. Temperature testing snaps to 1°C and supports clicking the track; RGB channels snap to integer values from 0 to 255. Native keyboard slider behavior and the shared focus visual remain available.

### Thermal profile editor

The editor's domain is 0–120°C. Its gradient track is 12 DIP high. Markers have 28 × 34 DIP hit areas, a 22 DIP halo, and a 12 DIP color center. Default halo stroke is 2 DIP; selected and focused markers use the accent with a 3 DIP stroke. A selected point is also represented in the wrapping list below the gradient.

Clicking a point selects it; dragging captures the pointer and moves the point at 0.1°C precision. Arrow keys move it by 1°C, Delete removes it when permitted, and double-clicking the track adds a point. The visible Add and Remove actions supply discoverable alternatives. Point spacing and minimum point count remain authoritative in the editor view model.

The track and test slider sample `ColorEngine.Map` at 241 positions. Its HSV interpolation and smoother-step easing match the active thermal profile. Rebuild this brush when the points change; a three-stop linear RGB approximation does not reproduce the implementation.

### Temperature instrument and history

The leading ring is 148 × 148 DIP with a 270-degree arc starting at 135 degrees and 10 DIP rounded strokes. Its range follows the profile's first and last points. A missing reading displays an em dash and a localized accessible unavailable value. The ring does not accept focus or input.

The chart shows the last 60 seconds, uses a fixed 0–120°C axis at 30°C intervals, and draws a 2 DIP ice-blue trace with a 3 DIP endpoint. Nonfinite readings are excluded. The dashboard profile bar draws a current-color marker and suppresses overlapping intermediate labels while retaining endpoint labels.

### Native color and lighting dialogs

The color dialog is an owned modal WPF window with the same dark surfaces. It provides the current swatch, HEX input, twelve preset swatches, and R/G/B sliders. Invalid HEX reveals localized feedback and disables Apply; the last valid color remains available. Apply is the default action, Cancel is the cancel action, and close cancels. Ownership follows the active window, including the lighting-test window.

Lighting test presents twelve equal schematic LEDs that all use the current preview color. The visible hint distinguishes this schematic from actual physical LED appearance. Its large temperature slider updates the shared engine preview and the test session. Apply saves the draft profile and completes cleanup; Cancel and close complete cleanup without applying the draft. An Apply error retains the draft and localized feedback for a retry.

### Localization

Visible interface and accessibility text resolve through `Strings.en-US.xaml` and `Strings.ru-RU.xaml` using dynamic resources. The service switches the language dictionary, resolves System to Russian for a Russian system culture and English otherwise, and falls back to English for string lookups. Literal product names, channel symbols, HEX, and temperature endpoints are retained where the source intentionally uses them.

**The Native State Rule.** Preserve WPF focus, validation, pointer capture, owned modal behavior, accessibility names, and the authoritative runtime/startup states when adding a screen. A visual state must describe the actual state of the application or Windows.

## Do's and Don'ts

### Do:

- **Do** use the extracted WPF DIP tokens and the existing shared styles.
- **Do** retain the fully labeled rail and measured content-width layout change.
- **Do** keep primary actions and validation feedback visible while content scrolls.
- **Do** derive lighting previews and temperature colors from the active profile and shared color engine.
- **Do** use localized resources and verify English and Russian layouts.
- **Do** preserve custom saved profiles and use the selected factory anchors only for defaults and explicit resets.
- **Do** expose native focus, selected, disabled, error, and modal completion states.

### Don't:

- **Don't** use the UI accent as a replacement for the factory cold-point color.
- **Don't** treat the retired magenta midpoint as the current factory default.
- **Don't** infer device capability or thermal-sync success from a decorative preview or a selected-looking switch.
- **Don't** replace the sampled HSV thermal gradient with a generic RGB gradient.
- **Don't** move fixed action footers inside their scroll viewers.
- **Don't** introduce undocumented font metrics, tonal ramps, motion durations, or shadow values into the normative extraction.
