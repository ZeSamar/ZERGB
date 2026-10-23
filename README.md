# ZERGB

A desktop widget for controlling your PC case RGB lighting directly through
the motherboard's HID controller — **no RGB Fusion or other vendor software
required**.

> ⚠️ This application was developed with AI assistance.

<img src="logo.png" width="120" alt="ZERGB">

![Screenshot](preview.png)

*[Русская версия](README.ru.md)*

## Features

- **Color** — a color wheel; the color is applied instantly.
- **Brightness** — a 0–100 % slider.
- **Effects** — 22 hardware modes animated by the controller itself: static,
  breathing, flash, rainbow, meteor, wave, ripple, random, transition, demo,
  beat and more.
- **Speed** — a 0–9 slider (10 protocol timing steps).
- **Music** — reacts to system audio (WASAPI loopback): color follows the
  bass/mid/treble balance, brightness follows loudness.
- **Presets** — 6 slots: left-click to apply, right-click to save.
- **Power** — the ⏻ button: turn the lighting off/on.
- **Channel fix** — an iOS-style toggle for boards with swapped R/G/B channels.
- **Always on top** — a toggle in the header.
- **Tray** — minimize, and a menu (open, always on top, channel fix,
  autostart, exit).
- **Hotkeys** (global):
  - `Ctrl+Alt+L` — toggle lighting,
  - `Ctrl+Alt+↑` / `Ctrl+Alt+↓` — brightness ±10 %,
  - `Ctrl+Alt+1…9` — the first nine effects.
- **Language** — Russian / English.

## Quick start

1. Download **`ZERGB.exe`** from the releases page.
2. Run it. Nothing to install — `.NET Framework 4.8` is already present in
   Windows 10/11.
3. Control your lighting. Launching again restores the already-open window.

## Requirements

- Windows 10/11 (x64).
- A motherboard with an ITE `VID_048D&PID_5702` HID lighting controller
  (tested on GIGABYTE B660 GAMING X DDR4). Other boards may use a different
  device path.
- RGB Fusion is **not needed** and should be closed — it writes to the same
  controller and causes flicker.

## How it works

The app opens the controller's HID device (`hid.dll`) and sends 64-byte
feature reports — the same protocol RGB Fusion uses.

Packet format (recovered by tracing the device traffic):

```
CC 20 FF 00 00 00 00 00 00 00 00 <mode> <brightness> 00 <B> <G> <R>
CC 28 FF            — apply
CC 21..27           — reset zones
```

Modes (`Mode_Sel`): `1` static, `2` breathing, `3` flash, `4` rainbow,
`5` meteor, `6` wave, `7` ripple, `8` random, `51` double flash,
`52` transition, `53` demo, `56` beat, `100` SWave, `102` SMeteor,
`103` shake, `104` explode, `105` recover, `106` double meteor,
`107` stack, `109` rain, `111` DWave, `112` SWave2.

## Building from source

Requires: Windows, `.NET Framework 4.8` (and its `csc.exe`), `NAudio.dll`
(in `lib/`), and the `wheel.png` / `icon.ico` assets.

```powershell
.\build.ps1
```

The script produces a single `ZERGB.exe` with all resources embedded
(markup, icon, NAudio — everything inside).

## Repository layout

| File | Purpose |
|---|---|
| `ZERGB.exe` | prebuilt application (v1.0.1) |
| `WidgetApp.cs` | all code (UI, HID, effects, music) |
| `ui.xaml` | window layout |
| `wheel.png` | color wheel image |
| `icon.ico`, `logo.png` | application icon |
| `mask_src.jpg`, `process_mask.py` | source photo and icon generator script |
| `lib/NAudio.dll` | audio capture dependency (MIT) |
| `build.ps1` | build script |
| `CHANGELOG.md` | changelog |

## SmartScreen note

The built exe is signed with a self-signed certificate. On other machines
Windows SmartScreen may show an "unrecognized app" warning — click
"More info → Run anyway". A certificate from a public CA is required for a
clean launch without warnings.

## License

MIT — see [LICENSE](LICENSE).
