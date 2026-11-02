using System.Collections.Concurrent;
using System.Diagnostics;

namespace RigGlow;

/// <summary>Lights a zone in red/green/blue blocks so you can check the LED count per fan.</summary>
public sealed class LedTest
{
    public string DeviceKey;
    public int Zone;
    public int LedsPerFan;
}

/// <summary>
/// Runs on its own thread: keeps a connection to OpenRGB, renders the chosen effect
/// and streams colours to every ticked device.
/// </summary>
public sealed class LightingEngine : IDisposable
{
    readonly AppSettings _s;
    readonly ConcurrentQueue<Action<OpenRgbClient>> _jobs = new();
    readonly object _devLock = new();
    readonly Dictionary<int, Rgb[]> _last = new();
    readonly Dictionary<int, long> _lastSent = new();
    readonly HashSet<int> _customMode = new();

    Thread _thread;
    volatile bool _running;
    volatile bool _reload = true;
    OpenRgbClient _client;
    List<RgbDevice> _devices = new();
    DateTime _launchedAt = DateTime.MinValue;

    public volatile string Status = "Starting...";
    public volatile bool IsConnected;
    public volatile LedTest Test;
    public event Action DevicesChanged;

    public LightingEngine(AppSettings settings) { _s = settings; }

    public IReadOnlyList<RgbDevice> Devices { get { lock (_devLock) return _devices.ToList(); } }

    public void Start()
    {
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "RigGlow lighting" };
        _thread.Start();
    }

    public void Enqueue(Action<OpenRgbClient> job) => _jobs.Enqueue(job);
    public void RequestReload() => _reload = true;
    public void Reconnect() => Enqueue(c => throw new IOException("Reconnecting"));

    /// <summary>Forget what we last sent so everything is re-sent on the next frame.</summary>
    public void Resend()
    {
        Enqueue(_ => { _last.Clear(); _customMode.Clear(); });
    }

    void Run()
    {
        var clock = Stopwatch.StartNew();
        while (_running)
        {
            try
            {
                if (_client == null || !_client.Connected)
                {
                    TryConnect();
                    if (!IsConnected) { Nap(2000); continue; }
                }

                _client.Poll();
                if (_client.DeviceListChanged) { _client.DeviceListChanged = false; _reload = true; }
                while (_jobs.TryDequeue(out var job)) job(_client);
                if (_reload) LoadDevices();
                RenderFrame(clock.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                Status = "Lost connection to OpenRGB (" + ex.Message + "). Retrying...";
                Log.Warn("Lighting: " + Status);
                Disconnect();
                Nap(2000);
                continue;
            }
            Nap(1000 / Math.Clamp(_s.Fps, 5, 60));
        }
        Disconnect();
    }

    void Nap(int ms)
    {
        while (ms > 0 && _running)
        {
            int step = Math.Min(ms, 50);
            Thread.Sleep(step);
            ms -= step;
        }
    }

    void TryConnect()
    {
        Disconnect();
        var c = new OpenRgbClient();
        try
        {
            c.Connect(_s.OpenRgbHost, _s.OpenRgbPort);
            _client = c;
            IsConnected = true;
            _reload = true;
            Status = "Connected to OpenRGB.";
            Log.Info($"Connected to OpenRGB at {_s.OpenRgbHost}:{_s.OpenRgbPort} (protocol {c.Protocol}).");
        }
        catch
        {
            c.Dispose();
            IsConnected = false;
            Status = MaybeLaunchOpenRgb();
        }
    }

    void Disconnect()
    {
        IsConnected = false;
        _client?.Dispose();
        _client = null;
        _last.Clear();
        _lastSent.Clear();
        _customMode.Clear();
    }

    string MaybeLaunchOpenRgb()
    {
        if ((DateTime.Now - _launchedAt).TotalSeconds < 45)
            return "Starting OpenRGB, this can take up to 30 seconds while it finds your devices...";

        bool running;
        try
        {
            var procs = Process.GetProcessesByName("OpenRGB");
            running = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
        }
        catch { running = false; }

        if (running)
            return "OpenRGB is open but its SDK server is off. Close OpenRGB completely (RigGlow will start it for you), or start its SDK server.";

        if (!_s.AutoLaunchOpenRgb)
            return "Can't reach OpenRGB. Start OpenRGB with its SDK server turned on.";

        var path = FindOpenRgb(_s.OpenRgbPath);
        if (path == null)
            return "OpenRGB isn't installed or couldn't be found. See the Setup check page.";

        try
        {
            Process.Start(new ProcessStartInfo(path, "--server --startminimized")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path)
            });
            _launchedAt = DateTime.Now;
            Log.Info("Started OpenRGB in server mode: " + path);
            return "Starting OpenRGB...";
        }
        catch (Exception ex)
        {
            _launchedAt = DateTime.Now;
            Log.Error("Couldn't start OpenRGB", ex);
            return "Couldn't start OpenRGB: " + ex.Message;
        }
    }

    public static string FindOpenRgb(string configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured.Trim().Trim('"'));
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(pf, "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(pf86, "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(local, "Programs", "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(home, "Downloads", "OpenRGB", "OpenRGB.exe"));
        candidates.Add(Path.Combine(home, "Desktop", "OpenRGB", "OpenRGB.exe"));
        foreach (var c in candidates)
            if (!string.IsNullOrEmpty(c) && File.Exists(c)) return c;
        return null;
    }

    void LoadDevices()
    {
        _reload = false;
        int n = _client.GetControllerCount();
        var list = new List<RgbDevice>();
        for (int i = 0; i < n; i++) list.Add(_client.GetController(i));

        // OpenRGB sometimes forgets the LED count of ARGB headers after a restart,
        // which leaves the fans dark. Put back the counts the user set in RigGlow.
        bool resized = false;
        foreach (var d in list)
            for (int z = 0; z < d.Zones.Count; z++)
            {
                var zone = d.Zones[z];
                if (!zone.Resizable) continue;
                var want = _s.GetZoneSize(AppSettings.ZoneKey(d, zone));
                if (want == null || want.Value == zone.LedsCount) continue;
                if (want.Value < zone.LedsMin || want.Value > zone.LedsMax) continue;
                _client.ResizeZone(d.Index, z, want.Value);
                resized = true;
            }
        if (resized)
        {
            Thread.Sleep(200);
            list.Clear();
            for (int i = 0; i < n; i++) list.Add(_client.GetController(i));
        }
        lock (_devLock) _devices = list;
        _last.Clear();
        _lastSent.Clear();
        _customMode.Clear();
        Log.Info("OpenRGB devices: " + string.Join(", ", list.Select(x => $"{x.Name} [{x.TypeName}, {x.LedCount} LEDs]")));
        Status = list.Count == 0
            ? "Connected to OpenRGB. Waiting for it to find devices..."
            : $"Connected to OpenRGB. {list.Count} device(s) found.";
        try { DevicesChanged?.Invoke(); } catch { }
    }

    void RenderFrame(double t)
    {
        List<RgbDevice> devices;
        lock (_devLock) devices = _devices;
        var test = Test;
        long now = Environment.TickCount64;

        foreach (var d in devices)
        {
            if (d.LedCount == 0) continue;
            bool testing = test != null && test.DeviceKey == d.Key;
            if (!testing && !_s.IsDeviceEnabled(d)) continue;

            if (_customMode.Add(d.Index)) _client.SetCustomMode(d.Index);

            bool isRam = d.Type == 1 && !testing;
            Rgb[] colors;
            if (testing) colors = Effects.RenderTest(d, test);
            else if (isRam && _s.Ram == RamMode.Static) colors = Effects.RenderStatic(_s, d);
            else if (isRam && _s.Ram == RamMode.Off) colors = new Rgb[d.LedCount];
            else colors = Effects.Render(_s, d, t);

            bool changed = !_last.TryGetValue(d.Index, out var prev) || !Same(prev, colors);
            _lastSent.TryGetValue(d.Index, out long lastMs);
            long age = now - lastMs;
            bool send;
            if (isRam)
                // RAM: at most 4 updates a second, and only a keep-alive every 30 s when nothing changes.
                send = (changed && age >= 250) || age > 30000;
            else
                send = changed || age > 2000;

            if (send)
            {
                _client.UpdateLeds(d.Index, colors);
                _last[d.Index] = colors;
                _lastSent[d.Index] = now;
            }
        }
    }

    static bool Same(Rgb[] a, Rgb[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!a[i].Equals(b[i])) return false;
        return true;
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(2000); } catch { }
    }
}

public static class Effects
{
    public static readonly (string Name, EffectType Type)[] Choices =
    {
        ("Static color", EffectType.Static),
        ("Breathing", EffectType.Breathing),
        ("Color cycle", EffectType.Spectrum),
        ("Rainbow wave", EffectType.RainbowWave),
        ("Two-color wave", EffectType.ColorWave),
        ("Gradient", EffectType.Gradient),
        ("Comet", EffectType.Comet),
        ("Off", EffectType.Off),
    };

    public static bool UsesColor1(EffectType t) =>
        t is EffectType.Static or EffectType.Breathing or EffectType.ColorWave or EffectType.Gradient or EffectType.Comet or EffectType.Temperature;

    public static bool UsesColor2(EffectType t) =>
        t is EffectType.ColorWave or EffectType.Gradient or EffectType.Comet;

    public static bool UsesSpeed(EffectType t) =>
        t is EffectType.Breathing or EffectType.Spectrum or EffectType.RainbowWave or EffectType.ColorWave or EffectType.Comet;

    public static int IndexOf(EffectType type)
    {
        for (int i = 0; i < Choices.Length; i++) if (Choices[i].Type == type) return i;
        return 0;
    }

    /// <summary>Color 1 at the chosen brightness (black when the effect is Off).</summary>
    public static Rgb[] RenderStatic(AppSettings s, RgbDevice d)
    {
        var a = new Rgb[d.LedCount];
        if (s.Effect == EffectType.Off) return a;
        var c = Rgb.FromHex(s.Color1).Scale(Math.Clamp(s.Brightness, 0, 100) / 100.0);
        for (int i = 0; i < a.Length; i++) a[i] = c;
        return a;
    }

    public static Rgb[] Render(AppSettings s, RgbDevice d, double t)
    {
        int n = d.LedCount;
        var a = new Rgb[n];
        var c1 = Rgb.FromHex(s.Color1);
        var c2 = Rgb.FromHex(s.Color2);
        double rate = 0.02 + Math.Clamp(s.Speed, 1, 100) / 100.0 * 0.6;   // cycles per second
        double bright = Math.Clamp(s.Brightness, 0, 100) / 100.0;

        switch (s.Effect == EffectType.Temperature ? EffectType.Static : s.Effect)
        {
            case EffectType.Static:
                for (int i = 0; i < n; i++) a[i] = c1;
                break;

            case EffectType.Breathing:
            {
                double level = 0.04 + 0.96 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * t * rate * 0.6));
                var c = c1.Scale(level);
                for (int i = 0; i < n; i++) a[i] = c;
                break;
            }

            case EffectType.Spectrum:
            {
                var c = Rgb.FromHsv(t * rate * 360, 1, 1);
                for (int i = 0; i < n; i++) a[i] = c;
                break;
            }

            case EffectType.RainbowWave:
                for (int i = 0; i < n; i++)
                    a[i] = Rgb.FromHsv((d.Positions[i] - t * rate) * 360, 1, 1);
                break;

            case EffectType.ColorWave:
                for (int i = 0; i < n; i++)
                {
                    double k = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (d.Positions[i] - t * rate));
                    a[i] = Rgb.Lerp(c1, c2, k);
                }
                break;

            case EffectType.Gradient:
                // Color 1 at the start of each zone fading to Color 2 at the end.
                for (int i = 0; i < n; i++) a[i] = Rgb.Lerp(c1, c2, d.Positions[i]);
                break;

            case EffectType.Comet:
            {
                // A bright head in Color 1 with a fading tail, over a faint Color 2 background.
                double head = (t * rate * 0.7) % 1.0;
                var bg = c2.Scale(0.08);
                for (int i = 0; i < n; i++)
                {
                    double dist = head - d.Positions[i];
                    if (dist < 0) dist += 1;
                    double k = dist < 0.3 ? Math.Pow(1 - dist / 0.3, 2) : 0;
                    a[i] = Rgb.Lerp(bg, c1, k);
                }
                break;
            }

            case EffectType.Off:
                for (int i = 0; i < n; i++) a[i] = Rgb.Black;
                break;
        }

        if (bright < 0.999)
            for (int i = 0; i < n; i++) a[i] = a[i].Scale(bright);
        return a;
    }

    /// <summary>
    /// Every LED off except the chosen zone, which is painted in alternating
    /// red / green / blue blocks of "LEDs per fan". If the count is right,
    /// each fan shows one solid colour. The zone's first LED is white.
    /// </summary>
    public static Rgb[] RenderTest(RgbDevice d, LedTest test)
    {
        var a = new Rgb[d.LedCount];
        if (test.Zone < 0 || test.Zone >= d.Zones.Count) return a;
        var z = d.Zones[test.Zone];
        int per = Math.Max(1, test.LedsPerFan);
        var palette = new[] { new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 60, 255) };
        for (int j = 0; j < z.LedsCount; j++)
        {
            int i = z.StartIndex + j;
            if (i >= a.Length) break;
            a[i] = j == 0 ? Rgb.White : palette[(j / per) % 3];
        }
        return a;
    }
}
