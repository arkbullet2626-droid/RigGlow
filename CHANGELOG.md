# Changelog

## 1.0.0 — first public release

- Lighting through OpenRGB: static color, breathing, color cycle, rainbow wave, two-color wave, gradient, comet, off.
- Per-device on/off, brightness and speed, quick color swatches.
- ARGB header LED count setup with a test pattern; counts are remembered and re-applied if OpenRGB forgets them.
- Gentle RAM lighting (static by default, or effects at most 4 updates a second) to protect DDR5 modules.
- Fan control through LibreHardwareMonitor: motherboard control, fixed speed, or a draggable temperature curve following CPU, GPU, the hotter of the two, or any sensor. Silent / Balanced / Performance presets.
- Safety: 100% fan speed if the CPU reaches 90°C or a temperature can't be read; fans handed back to the motherboard on exit, crash or uninstall.
- Setup check page: admin rights, OpenRGB, PawnIO, fan control, RGB RAM and conflicting apps, with fix-it links.
- System tray, start with Windows, start hidden, update check (can be turned off), log files.
- Installer and portable zip.
