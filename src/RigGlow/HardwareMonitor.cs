using LibreHardwareMonitor.Hardware;

namespace RigGlow;

public sealed class TempSource
{
    public string Id;
    public string Label;
    public override string ToString() => Label;
}

/// <summary>One motherboard fan header (e.g. "System Fan #1").</summary>
public sealed class FanHeader
{
    public string Name;
    internal ISensor ControlSensor;
    internal ISensor FanSensor;

    public float? Percent;        // what the board reports right now
    public float? Rpm;
    public float? SourceTemp;     // temperature the curve is following
    public bool UnderOurControl;  // false = BIOS is driving it
    public float Applied = -1;    // last % we sent
    internal double SmoothTemp = double.NaN;
    internal long LastWriteMs;
}

/// <summary>
/// Reads temperatures and drives motherboard fan headers through LibreHardwareMonitor.
/// Only CPU, GPU and motherboard are opened: RAM (SMBus), storage and USB controllers
/// such as the NZXT Kraken are deliberately left alone.
/// </summary>
public sealed class HardwareMonitor : IDisposable
{
    public const string AutoCpu = "auto:cpu";
    public const string AutoGpu = "auto:gpu";
    public const string AutoMax = "auto:max";

    /// <summary>If the CPU reaches this, every fan we control goes to 100%.</summary>
    public const float SafetyTempC = 90f;

    readonly AppSettings _s;
    readonly object _lock = new();
    Computer _pc;
    Thread _thread;
    volatile bool _running;
    bool _restored;

    List<FanHeader> _fans = new();
    Dictionary<string, ISensor> _temps = new();
    List<TempSource> _sources = new();
    string _cpuId, _gpuId;

    public volatile bool Ready;
    public volatile string Error;
    public volatile bool SafetyOverride;
    public string CpuName { get; private set; } = "";
    public string GpuName { get; private set; } = "";

    public HardwareMonitor(AppSettings settings) { _s = settings; }

    public IReadOnlyList<FanHeader> Fans { get { lock (_lock) return _fans.ToList(); } }
    public IReadOnlyList<TempSource> TempSources { get { lock (_lock) return _sources.ToList(); } }

    public void Start()
    {
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "RigGlow hardware" };
        _thread.Start();
    }

    void Run()
    {
        try
        {
            var pc = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
            };
            pc.Open();
            lock (_lock)
            {
                _pc = pc;
                UpdateAll();
                Discover();
            }
            Ready = true;
            Log.Info($"Hardware: CPU '{CpuName}', GPU '{GpuName}', fan headers: {string.Join(", ", _fans.Select(f => f.Name))}");
            if (_fans.Count == 0)
                Error = "No controllable fan headers were found. Make sure the PawnIO driver is installed and RigGlow runs as administrator.";
        }
        catch (Exception ex)
        {
            Error = "Fan control could not start: " + ex.Message + " (Is the PawnIO driver installed? See Setup check.)";
            Log.Error("Hardware monitor failed to start", ex);
            Ready = true;
            return;
        }

        while (_running)
        {
            try
            {
                lock (_lock)
                {
                    if (!_running) break;
                    UpdateAll();
                    ApplyFans();
                }
            }
            catch (Exception ex)
            {
                if (Error != ex.Message) Log.Error("Hardware loop", ex);
                Error = ex.Message;
            }

            for (int i = 0; i < 10 && _running; i++) Thread.Sleep(100);
        }
    }

    void UpdateAll()
    {
        foreach (var h in _pc.Hardware) UpdateRecursive(h);
    }

    static void UpdateRecursive(IHardware h)
    {
        h.Update();
        foreach (var sub in h.SubHardware) UpdateRecursive(sub);
    }

    static IEnumerable<IHardware> Flatten(IHardware h)
    {
        yield return h;
        foreach (var sub in h.SubHardware)
            foreach (var x in Flatten(sub)) yield return x;
    }

    void Discover()
    {
        var fans = new List<FanHeader>();
        var temps = new Dictionary<string, ISensor>();
        var sources = new List<TempSource>();
        ISensor cpuBest = null; int cpuRank = 99;
        ISensor gpuBest = null; int gpuRank = 99;

        foreach (var top in _pc.Hardware)
        {
            foreach (var h in Flatten(top))
            {
                foreach (var s in h.Sensors)
                {
                    if (s.SensorType != SensorType.Temperature) continue;
                    var id = s.Identifier.ToString();
                    temps[id] = s;
                    sources.Add(new TempSource { Id = id, Label = $"{h.Name} - {s.Name}" });

                    if (top.HardwareType == HardwareType.Cpu)
                    {
                        int rank = s.Name.Contains("Tctl") ? 0 : s.Name.Contains("Package") ? 1 : s.Name.StartsWith("Core") ? 2 : 3;
                        if (rank < cpuRank) { cpuRank = rank; cpuBest = s; CpuName = top.Name; }
                    }
                    else if (top.HardwareType == HardwareType.GpuNvidia || top.HardwareType == HardwareType.GpuAmd)
                    {
                        // Prefer the discrete NVIDIA card over the CPU's built-in Radeon graphics.
                        int rank = (top.HardwareType == HardwareType.GpuNvidia ? 0 : 10) + (s.Name == "GPU Core" ? 0 : 1);
                        if (rank < gpuRank) { gpuRank = rank; gpuBest = s; GpuName = top.Name; }
                    }
                }

                if (top.HardwareType != HardwareType.Motherboard) continue;

                var controls = h.Sensors.Where(x => x.SensorType == SensorType.Control && x.Control != null).ToList();
                var fanSensors = h.Sensors.Where(x => x.SensorType == SensorType.Fan).ToList();
                foreach (var c in controls)
                {
                    var f = fanSensors.FirstOrDefault(x => x.Name == c.Name)
                            ?? fanSensors.FirstOrDefault(x => x.Index == c.Index);
                    fans.Add(new FanHeader { Name = c.Name, ControlSensor = c, FanSensor = f });
                }
            }
        }

        _cpuId = cpuBest?.Identifier.ToString();
        _gpuId = gpuBest?.Identifier.ToString();

        sources.Insert(0, new TempSource { Id = AutoMax, Label = "Hottest of CPU and GPU" });
        sources.Insert(0, new TempSource { Id = AutoGpu, Label = "GPU (" + (GpuName == "" ? "not found" : GpuName) + ")" });
        sources.Insert(0, new TempSource { Id = AutoCpu, Label = "CPU (" + (CpuName == "" ? "not found" : CpuName) + ")" });

        _fans = fans;
        _temps = temps;
        _sources = sources;
    }

    float? Read(string id)
    {
        if (id == null) return null;
        var t = _temps;
        if (!t.TryGetValue(id, out var s)) return null;
        var v = s.Value;
        if (v == null || float.IsNaN(v.Value) || v.Value <= 0 || v.Value > 150) return null;
        return v;
    }

    public float? GetTemp(string sourceId)
    {
        if (string.IsNullOrEmpty(sourceId) || sourceId == AutoCpu) return Read(_cpuId);
        if (sourceId == AutoGpu) return Read(_gpuId);
        if (sourceId == AutoMax)
        {
            var a = Read(_cpuId); var b = Read(_gpuId);
            if (a == null) return b;
            if (b == null) return a;
            return Math.Max(a.Value, b.Value);
        }
        return Read(sourceId);
    }

    void ApplyFans()
    {
        long now = Environment.TickCount64;
        var cpu = Read(_cpuId);
        bool safety = cpu.HasValue && cpu.Value >= SafetyTempC;
        SafetyOverride = safety;

        foreach (var f in _fans)
        {
            f.Percent = f.ControlSensor.Value;
            f.Rpm = f.FanSensor?.Value;

            var fs = _s.GetFan(f.Name);
            if (fs.Mode == FanMode.Bios)
            {
                f.SourceTemp = null;
                if (f.UnderOurControl)
                {
                    try { f.ControlSensor.Control.SetDefault(); } catch { }
                    f.UnderOurControl = false;
                    f.Applied = -1;
                    f.SmoothTemp = double.NaN;
                }
                continue;
            }

            float target;
            bool immediate;
            if (fs.Mode == FanMode.Fixed)
            {
                f.SourceTemp = null;
                target = fs.FixedPercent;
                immediate = true;
            }
            else
            {
                var temp = GetTemp(fs.TempSource);
                f.SourceTemp = temp;
                if (temp == null)
                {
                    target = 100;   // can't see the temperature: be safe
                    immediate = true;
                }
                else
                {
                    f.SmoothTemp = double.IsNaN(f.SmoothTemp) ? temp.Value : f.SmoothTemp + (temp.Value - f.SmoothTemp) * 0.35;
                    target = Presets.Eval(fs.Curve, (float)f.SmoothTemp);
                    immediate = false;
                }
            }

            if (safety) { target = 100; immediate = true; }
            target = Math.Clamp(target, 0f, 100f);

            float applied;
            if (immediate || f.Applied < 0) applied = target;
            else if (target > f.Applied) applied = Math.Min(target, f.Applied + 15f);  // speed up quickly
            else applied = Math.Max(target, f.Applied - 4f);                           // slow down gently

            if (!f.UnderOurControl || Math.Abs(applied - f.Applied) >= 0.5f || now - f.LastWriteMs > 5000)
            {
                f.ControlSensor.Control.SetSoftware(applied);
                f.UnderOurControl = true;
                f.Applied = applied;
                f.LastWriteMs = now;
            }
        }
    }

    /// <summary>Hands every fan header back to the motherboard's own (BIOS) control.</summary>
    public void RestoreDefaults()
    {
        bool taken = false;
        try
        {
            Monitor.TryEnter(_lock, 3000, ref taken);
            if (_restored) return;
            foreach (var f in _fans)
            {
                if (!f.UnderOurControl) continue;
                try { f.ControlSensor.Control.SetDefault(); } catch { }
                f.UnderOurControl = false;
            }
            _restored = true;
        }
        finally
        {
            if (taken) Monitor.Exit(_lock);
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _thread?.Join(3000); } catch { }
        RestoreDefaults();
        try { _pc?.Close(); } catch { }
    }
}
