# Branding

- **App name:** Trvyam Management
- **Assets:** `Frontend/src/assets/branding/`
  - `logo-original.png` - as supplied (1536x1024, transparent)
  - `logo-full.png` - trimmed emblem + wordmark (953x1010), used on the splash screen at original proportions
  - `logo-emblem.png` - circular emblem only (673x666), source for the app icon
  - `app-icon-1024-preview.png` - preview of the icon on cream background
- **Primary brand colour:** `#253300` (dark olive, sampled from the logo). Proposed splash/background: warm off-white `#F7F5EC`, since the logo is dark on transparent.

## Limitations / to-do
- The logo is a **raster PNG**. It's fine for splash and icon, but a vector (SVG) master is recommended for sharp scaling and print; the edges are slightly rough at large sizes.
- The 1024x1024 icon is a preview. Final Android adaptive icons (foreground/background layers) and the iOS icon set will be generated during Phase 10/15.
- **Still needed:** bundle/application ID (proposed `com.trvyam.management`; confirm).
