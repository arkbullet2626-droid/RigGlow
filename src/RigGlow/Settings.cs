using System.Text.Json;
using System.Text.Json.Serialization;

namespace RigGlow;

// Temperature is kept only so older settings files still load; it now behaves like Static.
public enum EffectType { Static, Breathing, Spectrum, RainbowWave, ColorWave, Temperature, Off, Gradient, Comet }

public enum FanMode { Bios, Fixed, Curve }

/// <summary>How the RAM lighting is handled (DDR5 is sensitive to lots of traffic).</summary>
public enum RamMode { Static, SlowEffect, Off }

public sealed class CurvePoint
{
    public float T { get; set; }   // temperature °C
    public float P { get; set; }   // fan speed %
    public CurvePoint() { }
    public CurvePoint(float t, float p) { T = t; P = p; }
}

public static class Presets
{
    public static List<CurvePoint> Silent() => new()
    {
        new(30, 25), new(50, 30), new(60, 40), new(70, 55), new(80, 75), new(90, 100)
    };

    public static List<CurvePoint> Balanced() => new()
    {
        new(30, 30), new(50, 40), new(60, 50), new(70, 65), new(80, 85), new(90, 100)
    };

    public static List<CurvePoint> Performance() => new()
    {
        new(30, 40), new(50, 55), new(60, 70), new(70, 85), new(80, 100), new(90, 100)
    };

    /// <summary>Linear interpolation along the curve. Never throws.</summary>
    public static float Eval(List<CurvePoint> curve, float temp)
    {
        try
        {
            if (curve == null) return 100;
            var pts = curve.ToArray();
            if (pts.Length == 0) return 100;
            Array.Sort(pts, (a, b) => a.T.CompareTo(b.T));
            if (temp <= pts[0].T) return pts[0].P;
            if (temp >= pts[^1].T) return pts[^1].P;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var a = pts[i]; var b = pts[i + 1];
                if (temp <= b.T)
                {
                    float span = b.T - a.T;
                    if (span <= 0.001f) return b.P;
                    return a.P + (b.P - a.P) * (temp - a.T) / span;
                }
            }
            return pts[^1].P;
        }
        catch { return 100; }
    }
}

public sealed class FanSetting
{
    public FanMode Mode { get; set; } = FanMode.Bios;
    public int FixedPercent { get; set; } = 50;
    public string TempSource { get; set; } = HardwareMonitor.AutoCpu;
    public List<CurvePoint> Curve { get; set; } = Presets.Balanced();
}

public sealed class AppSettings
{
    // ---- Lighting ----
    public EffectType Effect { get; set; } = EffectType.Static;
    public string Color1 { get; set; } = "#00AEEF";
    public string Color2 { get; set; } = "#B400FF";
    public int Brightness { get; set; } = 100;
    public int Speed { get; set; } = 40;
    public int Fps { get; set; } = 30;
    public Dictionary<string, bool> DeviceEnabled { get; set; } = new();
    public RamMode Ram { get; set; } = RamMode.Static;

    /// <summary>LED count per ARGB header, e.g. "MSI PRO B650-S WIFI (MS-7E26)#JRAINBOW1" = 60.
    /// Re-applied every time OpenRGB (re)starts, because OpenRGB can forget them.</summary>
    public Dictionary<string, int> ZoneSizes { get; set; } = new();

    public static string ZoneKey(RgbDevice d, RgbZone z) => d.Name + "#" + z.Name;

    public int? GetZoneSize(string key)
    {
        lock (ZoneSizes) return ZoneSizes.TryGetValue(key, out var n) ? n : null;
    }

    public void SetZoneSize(string key, int size)
    {
        lock (ZoneSizes) ZoneSizes[key] = size;
    }

    // ---- Fans ----
    public Dictionary<string, FanSetting> Fans { get; set; } = new();

    // ---- OpenRGB ----
    public string OpenRgbPath { get; set; } = "";
    public bool AutoLaunchOpenRgb { get; set; } = true;
    public string OpenRgbHost { get; set; } = "127.0.0.1";
    public int OpenRgbPort { get; set; } = 6742;

    // ---- App ----
    /// <summary>When Windows starts RigGlow at sign-in, keep it in the tray. Manual launches always show the window.</summary>
    public bool StartMinimized { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool TrayHintShown { get; set; } = false;
    public bool AcceptedNotice { get; set; } = false;
    public bool CheckForUpdates { get; set; } = true;
    public string LastPage { get; set; } = "Lighting";

    static readonly object SaveLock = new();

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RigGlow");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (s != null)
                {
                    s.DeviceEnabled ??= new();
                    s.Fans ??= new();
                    s.ZoneSizes ??= new();
                    return s;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Settings file couldn't be read, starting with defaults: " + ex.Message);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            lock (SaveLock)
            {
                string json;
                lock (Fans) lock (DeviceEnabled) lock (ZoneSizes) json = JsonSerializer.Serialize(this, Options);
                Directory.CreateDirectory(Folder);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
        }
        catch (Exception ex) { Log.Warn("Couldn't save settings: " + ex.Message); }
    }

    public FanSetting GetFan(string header)
    {
        lock (Fans)
        {
            if (!Fans.TryGetValue(header, out var f))
            {
                f = new FanSetting();
                Fans[header] = f;
            }
            return f;
        }
    }

    public bool IsDeviceEnabled(RgbDevice d)
    {
        lock (DeviceEnabled)
            return DeviceEnabled.TryGetValue(d.Key, out var on) ? on : DefaultEnabled(d.Type);
    }

    public void SetDeviceEnabled(string key, bool on)
    {
        lock (DeviceEnabled) DeviceEnabled[key] = on;
    }

    /// <summary>
    /// By default we drive the motherboard (and its fan ARGB headers), RAM, LED strips and case lighting.
    /// Keyboards, mice, GPUs and AIO coolers start unticked, because they usually have their own software.
    /// </summary>
    static bool DefaultEnabled(int type) => type is 0 or 1 or 4 or 15;
}
