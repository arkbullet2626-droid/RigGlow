using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace RigGlow;

public enum CheckStatus { Ok, Warning, Problem, Info, Waiting }

public sealed class CheckItem
{
    public string Title;
    public CheckStatus State;
    public string Detail;
    public string ActionText;
    public Action Action;
}

/// <summary>Everything RigGlow needs to work, checked in plain language.</summary>
public static class SetupCheck
{
    /// <summary>Process name (without .exe) → friendly name of RGB / fan apps that fight over the same hardware.</summary>
    static readonly (string Process, string App)[] Conflicts =
    {
        ("SignalRgb", "SignalRGB"),
        ("SignalRgbLauncher", "SignalRGB"),
        ("iCUE", "Corsair iCUE"),
        ("ArmouryCrate", "ASUS Armoury Crate"),
        ("ArmouryCrate.Service", "ASUS Armoury Crate"),
        ("LightingService", "ASUS Aura lighting service"),
        ("MSI Center", "MSI Center (Mystic Light)"),
        ("MSI.CentralServer", "MSI Center (Mystic Light)"),
        ("MSI_Central_Service", "MSI Center (Mystic Light)"),
        ("MSI_Case_Service", "MSI Center (Mystic Light)"),
        ("RGBFusion", "Gigabyte RGB Fusion"),
        ("GCC", "Gigabyte Control Center"),
        ("Polychrome", "ASRock Polychrome"),
        ("Artemis.UI.Windows", "Artemis"),
        ("FanControl", "Fan Control (fans only)"),
    };

    public static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static bool PawnIoInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            if (key != null) return true;
        }
        catch { }
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return Directory.Exists(Path.Combine(pf, "PawnIO"));
    }

    public static List<string> RunningConflicts()
    {
        var found = new List<string>();
        foreach (var (proc, app) in Conflicts)
        {
            try
            {
                var ps = Process.GetProcessesByName(proc);
                if (ps.Length > 0 && !found.Contains(app)) found.Add(app);
                foreach (var p in ps) p.Dispose();
            }
            catch { }
        }
        return found;
    }

    public static List<CheckItem> Run(AppSettings s, LightingEngine engine, HardwareMonitor hw)
    {
        var items = new List<CheckItem>();

        bool admin = IsAdmin();
        items.Add(new CheckItem
        {
            Title = "Administrator rights",
            State = admin ? CheckStatus.Ok : CheckStatus.Problem,
            Detail = admin
                ? "RigGlow is running as administrator, which fan control and RAM lighting need."
                : "RigGlow isn't running as administrator, so fan control and RAM lighting won't work. Close it and start it again (it asks for admin automatically)."
        });

        var orgb = LightingEngine.FindOpenRgb(s.OpenRgbPath);
        items.Add(new CheckItem
        {
            Title = "OpenRGB installed",
            State = orgb != null ? CheckStatus.Ok : CheckStatus.Problem,
            Detail = orgb != null
                ? "Found at " + orgb
                : "RigGlow uses OpenRGB (free, open source) to talk to RGB hardware. Install OpenRGB 1.0 or newer, or set its location on the Settings page.",
            ActionText = orgb != null ? null : "Get OpenRGB",
            Action = orgb != null ? null : (Action)(() => AppInfo.OpenUrl(AppInfo.OpenRgbUrl))
        });

        var devices = engine.Devices;
        items.Add(new CheckItem
        {
            Title = "Lighting connection",
            State = engine.IsConnected ? (devices.Count > 0 ? CheckStatus.Ok : CheckStatus.Waiting) : CheckStatus.Warning,
            Detail = engine.IsConnected
                ? (devices.Count > 0 ? $"Connected to OpenRGB. {devices.Count} RGB device(s) found." : "Connected. OpenRGB is still looking for devices (this can take 30 seconds).")
                : engine.Status,
            ActionText = engine.IsConnected ? null : "Retry",
            Action = engine.IsConnected ? null : (Action)engine.Reconnect
        });

        bool pawn = PawnIoInstalled();
        items.Add(new CheckItem
        {
            Title = "PawnIO driver",
            State = pawn ? CheckStatus.Ok : CheckStatus.Problem,
            Detail = pawn
                ? "Installed. Needed for fan control and for RAM / motherboard lighting over SMBus."
                : "Not found. PawnIO is the free, signed driver that fan control and RAM lighting use. Install it, then restart RigGlow.",
            ActionText = pawn ? null : "Get PawnIO",
            Action = pawn ? null : (Action)(() => AppInfo.OpenUrl(AppInfo.PawnIoUrl))
        });

        CheckItem fan;
        if (!hw.Ready) fan = new CheckItem { Title = "Fan control", State = CheckStatus.Waiting, Detail = "Reading sensors..." };
        else if (!string.IsNullOrEmpty(hw.Error)) fan = new CheckItem { Title = "Fan control", State = CheckStatus.Problem, Detail = hw.Error };
        else
        {
            int n = hw.Fans.Count;
            fan = new CheckItem
            {
                Title = "Fan control",
                State = n > 0 ? CheckStatus.Ok : CheckStatus.Warning,
                Detail = n > 0 ? $"{n} motherboard fan header(s) can be controlled." : "No controllable fan headers found on this motherboard."
            };
        }
        items.Add(fan);

        var conflicts = RunningConflicts();
        items.Add(new CheckItem
        {
            Title = "Other RGB / fan apps",
            State = conflicts.Count == 0 ? CheckStatus.Ok : CheckStatus.Warning,
            Detail = conflicts.Count == 0
                ? "None running. RigGlow has the hardware to itself."
                : "Running now: " + string.Join(", ", conflicts) + ". These fight RigGlow over the same lights or fans. Quit them, and stop them starting with Windows (or uninstall them).",
            ActionText = conflicts.Count == 0 ? null : "Installed apps",
            Action = conflicts.Count == 0 ? null : (Action)(() => AppInfo.OpenUrl("ms-settings:appsfeatures"))
        });

        if (engine.IsConnected && devices.Count > 0)
        {
            bool ram = devices.Any(d => d.Type == 1);
            items.Add(new CheckItem
            {
                Title = "RGB RAM",
                State = ram ? CheckStatus.Ok : CheckStatus.Info,
                Detail = ram
                    ? "Found. RAM lighting is kept gentle by default (see Lighting > RAM lighting)."
                    : "No RGB RAM found. That's fine if your RAM has no lighting. If it does: make sure PawnIO is installed and no other RGB app is running, then restart the PC."
            });
        }

        return items;
    }
}
