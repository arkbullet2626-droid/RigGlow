<p align="center">
  <img src="docs/logo-256.png" width="120" alt="RigGlow logo">
</p>

<h1 align="center">RigGlow</h1>

<p align="center">
  <b>Free, open-source RGB lighting and fan control for Windows.</b><br>
  One small app for your fan lighting, RGB RAM, LED strips and fan speeds.<br>
  Made by <a href="https://opifexstudios.ca">Opifex Studios</a>.
</p>

<p align="center">
  <a href="../../releases/latest"><b>Download the latest version</b></a> ·
  <a href="#getting-started">Getting started</a> ·
  <a href="../../issues">Report a problem</a>
</p>

---

## Why RigGlow?

Most RGB and fan apps are heavy, tied to one brand, or ask you to pay for the good effects.
RigGlow is a lightweight alternative that does the everyday things well:

- **Lighting for many brands at once.** RigGlow drives your hardware through [OpenRGB](https://openrgb.org),
  which supports hundreds of motherboards, RAM kits, LED strips and controllers.
- **Fan curves that are easy to read.** Drag the points on a curve, or pick Silent, Balanced or Performance.
- **Safe by default.** Fans stay under your motherboard's control until you change them, and go back to it
  when RigGlow closes, crashes or is uninstalled.
- **Light on your PC.** No account, no ads, no telemetry. It sits in the system tray.

## Features

**Lighting**
- Effects: static color, breathing, color cycle, rainbow wave, two-color wave, gradient, comet, off
- Brightness and speed controls, quick color swatches and a full color picker
- Tick which devices follow the effect. Keyboards, mice, GPUs and AIO coolers start unticked so their own apps keep control
- **ARGB LED count helper:** a test pattern shows each fan in its own color so you can see when the count is right.
  RigGlow remembers the count and re-applies it if OpenRGB forgets it
- **Gentle RAM lighting:** DDR5 RAM is sensitive to heavy lighting traffic, so RAM stays on a static color by default,
  or follows effects at most 4 updates a second

**Fans**
- Per header: motherboard (BIOS) control, a fixed speed, or a temperature curve
- Follow the CPU, the GPU, whichever is hotter, or any temperature sensor
- Smooth speed changes: fast to speed up, gentle to slow down
- Safety: every controlled fan goes to 100% if the CPU reaches 90°C or a temperature can't be read

**App**
- **Setup check page** that explains in plain language what's missing or what might be fighting RigGlow
  (other RGB apps, missing drivers, no admin rights), with links to fix it
- System tray, start with Windows (no UAC prompt every time), start hidden
- Optional update check against this repository's releases; that's the only time RigGlow goes online
- Log files for troubleshooting

## Requirements

- Windows 10 (1809 or newer) or Windows 11, 64-bit
- [OpenRGB](https://openrgb.org) 1.0 or newer (free). RigGlow starts it in the background for you
- The [PawnIO](https://pawnio.eu) driver (free, signed) for fan control and RAM lighting. OpenRGB 1.0 offers to install it

RigGlow runs as administrator, because reading the motherboard's fan chip and RAM lighting needs it.

## Getting started

1. Install OpenRGB, and let it install PawnIO when it asks.
2. Quit other RGB and fan apps (SignalRGB, Corsair iCUE, ASUS Armoury Crate, MSI Center, ...) and stop them
   starting with Windows. Two apps controlling the same hardware will fight.
3. Download `RigGlow-x.y.z-Setup.exe` from [Releases](../../releases/latest) and run it.
   There's also a portable zip if you'd rather not install anything.
4. Open RigGlow and go to **Setup check**. Fix anything that isn't green.
5. On **Lighting**, set the LED count for each ARGB header with the test pattern, then pick an effect.
6. On **Fans**, pick a header and choose a fixed speed or a curve.

> **"Windows protected your PC"?** RigGlow isn't code-signed yet, so SmartScreen may warn you the first time.
> Click **More info → Run anyway**. You can check the download against `SHA256SUMS.txt` on the release page,
> or build it yourself from this source.

## Good to know

- **AIO pumps:** if a pump is plugged into the CPU Fan or Pump Fan header, leave that header on motherboard control.
- **RAM not showing up?** Make sure PawnIO is installed and no other RGB app is running, then restart.
  Some DDR5 kits aren't detected by current OpenRGB versions yet.
- **Keyboard stuck after trying RGB apps?** Unplug it and plug it back in to reset its lighting mode.
- Settings live in `%AppData%\RigGlow\settings.json`, logs in `%AppData%\RigGlow\logs`.

## Building from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and, for the installer, [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install Microsoft.DotNet.SDK.8` and `winget install JRSoftware.InnoSetup`).

```bat
build.bat 1.0.0
```

This makes `release\RigGlow-1.0.0-Setup.exe`, `release\RigGlow-1.0.0-portable.zip` and `release\SHA256SUMS.txt`.
Pushing a tag like `v1.0.1` builds and publishes a GitHub release automatically.

### How it fits together

| File | What it does |
|---|---|
| `OpenRgbClient.cs` | A small client for the OpenRGB SDK network protocol |
| `LightingEngine.cs` | Keeps the connection to OpenRGB alive and renders effects |
| `HardwareMonitor.cs` | Reads temperatures and drives fan headers through LibreHardwareMonitor, with the safety rules |
| `SetupCheck.cs` | The plain-language checks on the Setup check page |
| `MainForm.cs`, `Theme.cs`, `CurveEditor.cs` | The interface |

## Credits

RigGlow is built on [OpenRGB](https://openrgb.org), [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
and [PawnIO](https://pawnio.eu). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Licence

MIT © 2026 [Opifex Studios](https://opifexstudios.ca). RigGlow talks directly to hardware and is provided as-is,
without warranty of any kind.
