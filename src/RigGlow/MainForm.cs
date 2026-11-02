using System.Diagnostics;
using static RigGlow.Theme;

namespace RigGlow;

public sealed class MainForm : Form
{
    readonly AppSettings _s;
    readonly HardwareMonitor _hw;
    readonly LightingEngine _engine;
    readonly bool _startHidden;
    bool _allowShow;
    bool _exiting;

    readonly NotifyIcon _tray;
    readonly System.Windows.Forms.Timer _uiTimer;
    readonly System.Windows.Forms.Timer _saveTimer;

    // navigation
    readonly Panel _content;
    readonly Dictionary<string, Panel> _pages = new();
    readonly Dictionary<string, NavButton> _nav = new();
    string _current;
    Dot _lightDot, _fanDot;
    Label _lightState, _fanState;

    // lighting page
    Label _rgbStatus, _ramNote, _zoneInfo;
    ComboBox _effect, _ramMode, _ledDevice, _ledZone;
    Button _color1, _color2;
    TrackBar _bright, _speed;
    Label _brightVal, _speedVal;
    NumericUpDown _ledsPerFan, _fansOnZone;
    CheckedListBox _devList;
    CheckBox _testPattern;
    bool _suppressDev;

    // fans page
    Label _hwStatus, _fanTitle, _fanNow, _fixedVal;
    ListBox _fanList;
    ComboBox _fanMode, _fanTempSrc;
    TrackBar _fixed;
    Panel _fixedPanel, _curvePanel;
    CurveEditor _curve;
    bool _fansPopulated, _suppressFan;

    // setup page
    FlowLayoutPanel _checks;
    DateTime _lastCheck = DateTime.MinValue;

    // settings page
    TextBox _orgbPath;
    CheckBox _autoLaunch, _startup, _startMin, _toTray, _updates;

    // about page
    Label _updateInfo;
    string _updateUrl;

    static Icon _appIcon;
    public static Icon AppIcon
    {
        get
        {
            if (_appIcon != null) return _appIcon;
            try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            return _appIcon ??= SystemIcons.Application;
        }
    }

    public MainForm(AppSettings settings, HardwareMonitor hw, LightingEngine engine, bool startHidden)
    {
        _s = settings;
        _hw = hw;
        _engine = engine;
        _startHidden = startHidden;

        Text = $"{AppInfo.Name}  ·  {AppInfo.Company}";
        Icon = AppIcon;
        BackColor = Bg;
        ForeColor = Fg;
        Font = Body;
        MinimumSize = new Size(1060, 720);
        Size = new Size(1180, 800);
        StartPosition = FormStartPosition.CenterScreen;

        _saveTimer = new System.Windows.Forms.Timer { Interval = 800 };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _s.Save(); };
        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += (_, _) => RefreshUi();

        _content = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(24, 18, 10, 10) };
        Controls.Add(_content);
        Controls.Add(BuildSidebar());

        AddPage("Lighting", BuildLightingPage());
        AddPage("Fans", BuildFansPage());
        AddPage("Setup check", BuildSetupPage());
        AddPage("Settings", BuildSettingsPage());
        AddPage("About", BuildAboutPage());
        ShowPage(_pages.ContainsKey(_s.LastPage ?? "") ? _s.LastPage : "Lighting");

        _tray = new NotifyIcon { Icon = AppIcon, Text = AppInfo.Name, Visible = true, ContextMenuStrip = BuildTrayMenu() };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_updateUrl != null) AppInfo.OpenUrl(_updateUrl);
            else ShowFromTray();
        };

        _uiTimer.Start();

        _engine.DevicesChanged += () =>
        {
            try { if (IsHandleCreated) BeginInvoke(new Action(RefreshDevices)); } catch { }
        };

        if (!_startHidden) _allowShow = true;
        if (!IsHandleCreated) CreateHandle();
        RefreshDevices();
        RefreshUi();

        // Keep "start with Windows" pointing at this copy of RigGlow (e.g. after moving or reinstalling).
        Task.Run(() => { if (Startup.IsEnabled()) Startup.Set(true, _s.StartMinimized); });

        if (_s.CheckForUpdates) _ = CheckUpdatesAsync(silent: true, delayMs: 8000);
    }

    void MarkDirty() { _saveTimer.Stop(); _saveTimer.Start(); }

    // ================================================================== layout

    Panel BuildSidebar()
    {
        var side = new Panel { Dock = DockStyle.Left, Width = 210, BackColor = Sidebar };

        var brand = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Sidebar };
        var logo = new LogoBox(40) { Location = new Point(18, 24) };
        var name = new Label { Text = AppInfo.Name, Font = new Font("Segoe UI Semibold", 15f), ForeColor = Fg, AutoSize = true, Location = new Point(64, 22), BackColor = Sidebar };
        var by = new Label { Text = "by " + AppInfo.Company, Font = Small, ForeColor = Muted, AutoSize = true, Location = new Point(66, 52), BackColor = Sidebar };
        brand.Controls.AddRange(new Control[] { logo, name, by });

        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Sidebar, Padding = new Padding(0, 6, 0, 0) };
        foreach (var (title, glyph) in new[] { ("Lighting", "☀"), ("Fans", "❄"), ("Setup check", "✔"), ("Settings", "⚙"), ("About", "ℹ") })
        {
            var b = new NavButton(title, glyph) { Width = 210 };
            b.Click += (_, _) => ShowPage(title);
            _nav[title] = b;
            nav.Controls.Add(b);
        }

        var status = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 86, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Sidebar, Padding = new Padding(14, 8, 6, 8) };
        _lightDot = new Dot();
        _lightState = new Label { AutoSize = true, ForeColor = Muted, Font = Small, Margin = new Padding(0, 7, 0, 0), Text = "Lighting: starting" };
        _fanDot = new Dot();
        _fanState = new Label { AutoSize = true, ForeColor = Muted, Font = Small, Margin = new Padding(0, 7, 0, 0), Text = "Fans: starting" };
        status.Controls.Add(Row(_lightDot, _lightState));
        status.Controls.Add(Row(_fanDot, _fanState));

        side.Controls.Add(nav);
        side.Controls.Add(status);
        side.Controls.Add(brand);
        return side;
    }

    void AddPage(string title, Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages[title] = page;
        _content.Controls.Add(page);
    }

    void ShowPage(string title)
    {
        if (!_pages.ContainsKey(title)) return;
        foreach (var kv in _pages) kv.Value.Visible = kv.Key == title;
        foreach (var kv in _nav) kv.Value.Active = kv.Key == title;
        _current = title;
        if (_s.LastPage != title) { _s.LastPage = title; MarkDirty(); }
        if (title == "Setup check") RefreshChecks();
        if (title == "Fans") UpdateFanNow();
    }

    static Panel Page(string title, string subtitle, Control body)
    {
        var page = new Panel { BackColor = Bg };
        var head = new Panel { Dock = DockStyle.Top, Height = 78, BackColor = Bg };
        head.Controls.Add(new Label { Text = title, Font = PageTitle, ForeColor = Fg, AutoSize = true, Location = new Point(0, 0) });
        head.Controls.Add(new Label { Text = subtitle, ForeColor = Muted, AutoSize = true, Location = new Point(2, 42) });
        body.Dock = DockStyle.Fill;
        page.Controls.Add(body);
        page.Controls.Add(head);
        return page;
    }

    static TableLayoutPanel TwoColumns(Control left, Control right, int leftWidth = 0)
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, BackColor = Bg, Margin = new Padding(0) };
        if (leftWidth > 0)
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, leftWidth));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        }
        else
        {
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Dock = DockStyle.Fill;
        right.Dock = DockStyle.Fill;
        grid.Controls.Add(left, 0, 0);
        grid.Controls.Add(right, 1, 0);
        return grid;
    }

    // ================================================================== lighting

    Panel BuildLightingPage()
    {
        var left = Column();
        var right = Column();

        _rgbStatus = Note("Starting...", 470);
        _rgbStatus.ForeColor = Warn;
        left.Controls.Add(_rgbStatus);

        left.Controls.Add(H("Effect"));
        _effect = Combo(230);
        foreach (var choice in Effects.Choices) _effect.Items.Add(choice.Name);
        _effect.SelectedIndex = Effects.IndexOf(_s.Effect);
        _effect.SelectedIndexChanged += (_, _) =>
        {
            if (_effect.SelectedIndex < 0) return;
            _s.Effect = Effects.Choices[_effect.SelectedIndex].Type;
            UpdateEffectControls();
            MarkDirty();
        };
        left.Controls.Add(Row(Lbl("Effect", width: 90), _effect));

        _color1 = Btn("", 64);
        _color1.BackColor = Rgb.FromHex(_s.Color1).ToColor();
        _color1.Click += (_, _) => PickColor(_color1, c => _s.Color1 = c);
        _color2 = Btn("", 64);
        _color2.BackColor = Rgb.FromHex(_s.Color2).ToColor();
        _color2.Click += (_, _) => PickColor(_color2, c => _s.Color2 = c);
        left.Controls.Add(Row(Lbl("Color 1", width: 90), _color1, Lbl("Color 2"), _color2));

        var quick = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(420, 0), BackColor = Bg };
        foreach (var hex in new[] { "#FF0000", "#FF6A00", "#FFD000", "#00FF40", "#00E5FF", "#0050FF", "#8A00FF", "#FF00C8", "#FFFFFF" })
        {
            var sw = new Button
            {
                Width = 32,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Rgb.FromHex(hex).ToColor(),
                Margin = new Padding(3),
                Cursor = Cursors.Hand
            };
            sw.FlatAppearance.BorderColor = Bg;
            var h = hex;
            sw.Click += (_, _) =>
            {
                _s.Color1 = h;
                _color1.BackColor = Rgb.FromHex(h).ToColor();
                MarkDirty();
            };
            quick.Controls.Add(sw);
        }
        left.Controls.Add(Row(Lbl("Quick", width: 90), quick));

        _bright = Slider(_s.Brightness);
        _brightVal = Lbl(_s.Brightness + "%");
        _bright.ValueChanged += (_, _) => { _s.Brightness = _bright.Value; _brightVal.Text = _bright.Value + "%"; MarkDirty(); };
        left.Controls.Add(Row(Lbl("Brightness", width: 90), _bright, _brightVal));

        _speed = Slider(_s.Speed, min: 1);
        _speedVal = Lbl(_s.Speed + "");
        _speed.ValueChanged += (_, _) => { _s.Speed = _speed.Value; _speedVal.Text = _speed.Value + ""; MarkDirty(); };
        left.Controls.Add(Row(Lbl("Speed", width: 90), _speed, _speedVal));

        left.Controls.Add(H("RAM lighting"));
        left.Controls.Add(Note("DDR5 RAM is sensitive to a lot of lighting traffic, so RigGlow treats it gently. Static is the safest choice. \"Follow the effect\" updates the RAM at most 4 times a second, so animations look a little steppier on the sticks."));
        _ramMode = Combo(300);
        _ramMode.Items.AddRange(new object[] { "Static Color 1 (safest)", "Follow the effect, slowed down", "Off" });
        _ramMode.SelectedIndex = Math.Clamp((int)_s.Ram, 0, 2);
        _ramMode.SelectedIndexChanged += (_, _) =>
        {
            if (_ramMode.SelectedIndex < 0) return;
            _s.Ram = (RamMode)_ramMode.SelectedIndex;
            MarkDirty();
        };
        left.Controls.Add(Row(Lbl("RAM", width: 90), _ramMode));

        // ---- devices
        right.Controls.Add(H("Devices"));
        right.Controls.Add(Note("Ticked devices follow the effect. Keyboards, mice, GPUs and AIO coolers start unticked because they usually have their own software."));
        _devList = new CheckedListBox
        {
            Width = 470,
            Height = 140,
            BackColor = Card,
            ForeColor = Fg,
            BorderStyle = BorderStyle.None,
            CheckOnClick = true,
            IntegralHeight = false
        };
        _devList.ItemCheck += (_, e) =>
        {
            if (_suppressDev) return;
            if (_devList.Items[e.Index] is RgbDevice d)
            {
                _s.SetDeviceEnabled(d.Key, e.NewValue == CheckState.Checked);
                MarkDirty();
            }
        };
        right.Controls.Add(_devList);

        _ramNote = Note("", 470);
        _ramNote.ForeColor = Warn;
        right.Controls.Add(_ramNote);

        var reconnect = Btn("Reconnect to OpenRGB");
        reconnect.Click += (_, _) => _engine.Reconnect();
        right.Controls.Add(reconnect);

        // ---- LED count setup
        right.Controls.Add(H("ARGB header LED count"));
        right.Controls.Add(Note("Addressable (ARGB) headers can't tell how many LEDs are plugged in. Enter LEDs per fan (check the fan's specs, 8-20 is typical) and how many fans share that header. Turn on the test pattern: if the count is right, every fan shows one solid color (red, green, blue...)."));

        _ledDevice = Combo(340);
        _ledDevice.SelectedIndexChanged += (_, _) => FillZones();
        right.Controls.Add(Row(Lbl("Device", width: 120), _ledDevice));

        _ledZone = Combo(220);
        _ledZone.SelectedIndexChanged += (_, _) => { UpdateZoneInfo(); UpdateTest(); };
        right.Controls.Add(Row(Lbl("Header", width: 120), _ledZone));
        _zoneInfo = Note("", 470);
        right.Controls.Add(_zoneInfo);

        _ledsPerFan = Num(1, 60, 10);
        _fansOnZone = Num(1, 12, 3);
        _ledsPerFan.ValueChanged += (_, _) => { UpdateZoneInfo(); UpdateTest(); };
        _fansOnZone.ValueChanged += (_, _) => UpdateZoneInfo();
        right.Controls.Add(Row(Lbl("LEDs per fan", width: 120), _ledsPerFan, Lbl("Fans on header"), _fansOnZone));

        var apply = Btn("Apply LED count", primary: true);
        apply.Click += (_, _) => ApplyZoneSize();
        _testPattern = Check("Show test pattern", false);
        _testPattern.CheckedChanged += (_, _) => UpdateTest();
        right.Controls.Add(Row(apply, _testPattern));

        UpdateEffectControls();
        return Page("Lighting", "Pick an effect for your fans, RAM, LED strips and motherboard.", TwoColumns(left, right));
    }

    void UpdateEffectControls()
    {
        var t = _s.Effect;
        _color1.Enabled = Effects.UsesColor1(t);
        _color2.Enabled = Effects.UsesColor2(t);
        _speed.Enabled = Effects.UsesSpeed(t);
        _color1.FlatAppearance.BorderColor = _color1.Enabled ? Border : Bg;
        _color2.FlatAppearance.BorderColor = _color2.Enabled ? Border : Bg;
    }

    void PickColor(Button target, Action<string> set)
    {
        using var dlg = new ColorDialog { Color = target.BackColor, FullOpen = true, AnyColor = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        set(Rgb.FromColor(dlg.Color).ToHex());
        target.BackColor = dlg.Color;
        MarkDirty();
    }

    sealed class ZoneItem
    {
        public int Index;
        public RgbZone Zone;
        public override string ToString() => $"{Zone.Name} ({Zone.LedsCount} LEDs)";
    }

    void RefreshDevices()
    {
        var devices = _engine.Devices;

        _suppressDev = true;
        _devList.BeginUpdate();
        _devList.Items.Clear();
        foreach (var d in devices) _devList.Items.Add(d, _s.IsDeviceEnabled(d));
        _devList.EndUpdate();
        _suppressDev = false;

        bool noRam = _engine.IsConnected && devices.Count > 0 && !devices.Any(d => d.Type == 1);
        _ramNote.Text = noRam
            ? "No RGB RAM found. If your RAM has lighting: install PawnIO, make sure no other RGB app is running (see Setup check), then restart the PC. Some DDR5 kits aren't detected by OpenRGB yet."
            : "";
        _ramNote.Visible = noRam;

        string prevKey = (_ledDevice.SelectedItem as RgbDevice)?.Key;
        int prevZone = (_ledZone.SelectedItem as ZoneItem)?.Index ?? -1;
        _ledDevice.Items.Clear();
        foreach (var d in devices.Where(d => d.Zones.Any(z => z.Resizable))) _ledDevice.Items.Add(d);
        if (_ledDevice.Items.Count > 0)
        {
            int sel = 0;
            for (int i = 0; i < _ledDevice.Items.Count; i++)
                if (((RgbDevice)_ledDevice.Items[i]).Key == prevKey) sel = i;
            _ledDevice.SelectedIndex = sel;
            if (prevZone >= 0)
                for (int i = 0; i < _ledZone.Items.Count; i++)
                    if (((ZoneItem)_ledZone.Items[i]).Index == prevZone) _ledZone.SelectedIndex = i;
        }
        else
        {
            _ledZone.Items.Clear();
            _zoneInfo.Text = _engine.IsConnected ? "No adjustable ARGB headers found." : "";
        }
    }

    void FillZones()
    {
        _ledZone.Items.Clear();
        if (_ledDevice.SelectedItem is not RgbDevice d) return;
        for (int i = 0; i < d.Zones.Count; i++)
            if (d.Zones[i].Resizable) _ledZone.Items.Add(new ZoneItem { Index = i, Zone = d.Zones[i] });
        if (_ledZone.Items.Count > 0) _ledZone.SelectedIndex = 0;
    }

    void UpdateZoneInfo()
    {
        if (_ledZone.SelectedItem is not ZoneItem z) { _zoneInfo.Text = ""; return; }
        int total = (int)(_ledsPerFan.Value * _fansOnZone.Value);
        _zoneInfo.Text = $"Currently {z.Zone.LedsCount} LEDs on {z.Zone.Name}. New total would be {total} (this header allows up to {z.Zone.LedsMax}).";
    }

    void ApplyZoneSize()
    {
        if (_ledDevice.SelectedItem is not RgbDevice d || _ledZone.SelectedItem is not ZoneItem z) return;
        int total = (int)(_ledsPerFan.Value * _fansOnZone.Value);
        total = Math.Clamp(total, Math.Max(1, z.Zone.LedsMin), z.Zone.LedsMax);
        int dev = d.Index, zone = z.Index;
        _s.SetZoneSize(AppSettings.ZoneKey(d, z.Zone), total);
        MarkDirty();
        _engine.Enqueue(c => c.ResizeZone(dev, zone, total));
        _engine.RequestReload();
        _zoneInfo.Text = $"Setting {z.Zone.Name} to {total} LEDs...";
        Log.Info($"LED count for {d.Name} / {z.Zone.Name} set to {total}");
    }

    void UpdateTest()
    {
        if (_testPattern == null) return;
        if (_testPattern.Checked && _ledDevice.SelectedItem is RgbDevice d && _ledZone.SelectedItem is ZoneItem z)
            _engine.Test = new LedTest { DeviceKey = d.Key, Zone = z.Index, LedsPerFan = (int)_ledsPerFan.Value };
        else
            _engine.Test = null;
    }

    // ================================================================== fans

    sealed class FanItem
    {
        public FanHeader Header;
        public override string ToString() => Header.Name;
    }

    Panel BuildFansPage()
    {
        var left = Column();
        var right = Column();

        _hwStatus = Note("Reading sensors...", 300);
        left.Controls.Add(_hwStatus);

        _fanList = new ListBox
        {
            Width = 300,
            Height = 420,
            BackColor = Card,
            ForeColor = Fg,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 50,
            IntegralHeight = false
        };
        _fanList.DrawItem += DrawFanItem;
        _fanList.SelectedIndexChanged += (_, _) => LoadFanDetail();
        left.Controls.Add(_fanList);
        left.Controls.Add(Note("Fans on a hub or splitter show up as one header. Headers showing 0 RPM have nothing plugged in.", 300));
        var allBios = Btn("Give all fans back to the motherboard");
        allBios.Click += (_, _) =>
        {
            foreach (var f in _hw.Fans) _s.GetFan(f.Name).Mode = FanMode.Bios;
            MarkDirty();
            LoadFanDetail();
            _fanList.Invalidate();
        };
        left.Controls.Add(allBios);

        _fanTitle = H("Select a fan header");
        _fanTitle.Margin = new Padding(2, 0, 2, 6);
        right.Controls.Add(_fanTitle);

        _fanMode = Combo(270);
        _fanMode.Items.AddRange(new object[] { "Motherboard (BIOS) control", "Fixed speed", "Follow a temperature curve" });
        _fanMode.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressFan || SelectedFanSetting() is not FanSetting fs) return;
            fs.Mode = (FanMode)_fanMode.SelectedIndex;
            ShowModePanels(fs.Mode);
            _fanList.Invalidate();
            MarkDirty();
        };
        right.Controls.Add(Row(Lbl("Mode", width: 90), _fanMode));

        _fixed = Slider(50, 380);
        _fixedVal = Lbl("50%");
        _fixed.ValueChanged += (_, _) =>
        {
            _fixedVal.Text = _fixed.Value + "%";
            if (_suppressFan || SelectedFanSetting() is not FanSetting fs) return;
            fs.FixedPercent = _fixed.Value;
            MarkDirty();
        };
        _fixedPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Bg };
        _fixedPanel.Controls.Add(Row(Lbl("Speed", width: 90), _fixed, _fixedVal));
        right.Controls.Add(_fixedPanel);

        _fanTempSrc = Combo(370);
        _fanTempSrc.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressFan || SelectedFanSetting() is not FanSetting fs) return;
            if (_fanTempSrc.SelectedItem is TempSource ts) { fs.TempSource = ts.Id; MarkDirty(); }
        };
        _curve = new CurveEditor { Width = 600, Height = 300, Margin = new Padding(3, 6, 3, 6) };
        _curve.Changed += MarkDirty;

        var silent = Btn("Silent");
        var balanced = Btn("Balanced");
        var perf = Btn("Performance");
        silent.Click += (_, _) => ApplyPreset(Presets.Silent());
        balanced.Click += (_, _) => ApplyPreset(Presets.Balanced());
        perf.Click += (_, _) => ApplyPreset(Presets.Performance());

        _curvePanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Bg };
        _curvePanel.Controls.Add(Row(Lbl("Follow", width: 90), _fanTempSrc));
        _curvePanel.Controls.Add(_curve);
        _curvePanel.Controls.Add(Row(Lbl("Presets", width: 90), silent, balanced, perf));
        _curvePanel.Controls.Add(Note("Drag the dots to shape the curve. Case fans often work best following \"Hottest of CPU and GPU\", because the graphics card heats the case too.", 600));
        right.Controls.Add(_curvePanel);

        _fanNow = Lbl("", true);
        right.Controls.Add(_fanNow);

        right.Controls.Add(Note($"Safety: if the CPU reaches {HardwareMonitor.SafetyTempC:0}°C, every fan RigGlow controls goes to 100%. If a temperature can't be read, that fan goes to 100%. When RigGlow closes, all fans go back to motherboard control.", 600));
        var pump = Note("If an AIO or water-cooling pump is plugged into \"CPU Fan\" or \"Pump Fan\", leave that header on motherboard control. A pump should never be slowed down.", 600);
        pump.ForeColor = Warn;
        right.Controls.Add(pump);

        _fixedPanel.Visible = false;
        _curvePanel.Visible = false;
        _fanMode.Enabled = false;
        return Page("Fans", "Fixed speeds or temperature curves for your motherboard's fan headers.", TwoColumns(left, right, 330));
    }

    void DrawFanItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _fanList.Items.Count) return;
        var f = ((FanItem)_fanList.Items[e.Index]).Header;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(selected ? Color.FromArgb(0, 84, 122) : Card))
            e.Graphics.FillRectangle(bg, e.Bounds);
        using (var line = new Pen(Bg))
            e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        var fs = _s.GetFan(f.Name);
        string mode = fs.Mode switch
        {
            FanMode.Fixed => $"Fixed {fs.FixedPercent}%",
            FanMode.Curve => "Curve",
            _ => "BIOS"
        };
        string rpm = f.Rpm.HasValue ? $"{f.Rpm.Value:0} RPM" : "- RPM";
        string pct = f.Percent.HasValue ? $"{f.Percent.Value:0}%" : "-";

        using var fg = new SolidBrush(Fg);
        using var muted = new SolidBrush(selected ? Color.FromArgb(200, 225, 240) : Muted);
        e.Graphics.DrawString(f.Name, Strong, fg, e.Bounds.X + 12, e.Bounds.Y + 6);
        e.Graphics.DrawString($"{rpm}   ·   {pct}   ·   {mode}", Font, muted, e.Bounds.X + 12, e.Bounds.Y + 27);
    }

    FanHeader SelectedFan() => (_fanList.SelectedItem as FanItem)?.Header;
    FanSetting SelectedFanSetting() => SelectedFan() is FanHeader f ? _s.GetFan(f.Name) : null;

    void PopulateFans()
    {
        var fans = _hw.Fans;
        _fanList.BeginUpdate();
        _fanList.Items.Clear();
        foreach (var f in fans) _fanList.Items.Add(new FanItem { Header = f });
        _fanList.EndUpdate();

        _fanTempSrc.Items.Clear();
        foreach (var src in _hw.TempSources) _fanTempSrc.Items.Add(src);

        if (_fanList.Items.Count > 0) _fanList.SelectedIndex = 0;
    }

    static void SelectTemp(ComboBox combo, string id)
    {
        for (int i = 0; i < combo.Items.Count; i++)
            if (((TempSource)combo.Items[i]).Id == id) { combo.SelectedIndex = i; return; }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    void LoadFanDetail()
    {
        var f = SelectedFan();
        if (f == null) return;
        var fs = _s.GetFan(f.Name);
        _suppressFan = true;
        _fanTitle.Text = f.Name;
        _fanMode.Enabled = true;
        _fanMode.SelectedIndex = (int)fs.Mode;
        _fixed.Value = Math.Clamp(fs.FixedPercent, 0, 100);
        _fixedVal.Text = fs.FixedPercent + "%";
        SelectTemp(_fanTempSrc, fs.TempSource);
        fs.Curve ??= Presets.Balanced();
        _curve.Points = fs.Curve;
        _suppressFan = false;
        ShowModePanels(fs.Mode);
        UpdateFanNow();
    }

    void ShowModePanels(FanMode mode)
    {
        _fixedPanel.Visible = mode == FanMode.Fixed;
        _curvePanel.Visible = mode == FanMode.Curve;
    }

    void ApplyPreset(List<CurvePoint> points)
    {
        if (SelectedFanSetting() is not FanSetting fs) return;
        fs.Curve = points;
        _curve.Points = points;
        MarkDirty();
    }

    void UpdateFanNow()
    {
        var f = SelectedFan();
        if (f == null) { _fanNow.Text = ""; return; }
        var fs = _s.GetFan(f.Name);
        string text = $"Now: {(f.Rpm.HasValue ? f.Rpm.Value.ToString("0") : "-")} RPM at {(f.Percent.HasValue ? f.Percent.Value.ToString("0") : "-")}%";
        if (fs.Mode == FanMode.Curve)
        {
            var t = f.SourceTemp ?? _hw.GetTemp(fs.TempSource);
            text += t.HasValue ? $"   ·   temperature {t.Value:0.0}°C" : "   ·   temperature not available (running at 100%)";
            _curve.CurrentTemp = t;
            _curve.CurrentPercent = f.UnderOurControl ? f.Applied : f.Percent;
            _curve.Invalidate();
        }
        if (fs.Mode == FanMode.Bios) text += "   ·   the motherboard is controlling this header";
        if (_hw.SafetyOverride) text += "   ·   SAFETY: CPU is very hot, fans at 100%";
        _fanNow.Text = text;
    }

    // ================================================================== setup check

    Panel BuildSetupPage()
    {
        var col = Column();
        var rerun = Btn("Run checks again", primary: true);
        rerun.Click += (_, _) => RefreshChecks();
        var logs = Btn("Open log folder");
        logs.Click += (_, _) => AppInfo.OpenFolder(Log.Folder);
        var report = Btn("Report a problem");
        report.Click += (_, _) => AppInfo.OpenUrl(AppInfo.IssuesUrl);
        col.Controls.Add(Row(rerun, logs, report));

        _checks = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Bg, Margin = new Padding(0, 8, 0, 0) };
        col.Controls.Add(_checks);
        return Page("Setup check", "Everything RigGlow needs, and anything that could get in its way.", col);
    }

    void RefreshChecks()
    {
        if (_checks == null) return;
        _lastCheck = DateTime.Now;
        List<CheckItem> items;
        try { items = SetupCheck.Run(_s, _engine, _hw); }
        catch (Exception ex) { Log.Error("Setup check", ex); return; }

        _checks.SuspendLayout();
        var old = _checks.Controls.Cast<Control>().ToList();
        _checks.Controls.Clear();
        foreach (var c in old) c.Dispose();
        foreach (var item in items) _checks.Controls.Add(CheckCard(item));
        _checks.ResumeLayout();
    }

    Control CheckCard(CheckItem item)
    {
        var card = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 2,
            Width = 760,
            AutoSize = true,
            BackColor = Card,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 0, 0, 8)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var dot = new Dot { DotColor = StateColor(item.State), Margin = new Padding(0, 4, 0, 0) };
        var title = new Label { Text = item.Title, Font = Strong, ForeColor = Fg, AutoSize = true, Margin = new Padding(0, 0, 0, 2), BackColor = Card };
        var detail = new Label { Text = item.Detail ?? "", ForeColor = Muted, AutoSize = true, MaximumSize = new Size(560, 0), BackColor = Card };
        card.Controls.Add(dot, 0, 0);
        card.Controls.Add(title, 1, 0);
        card.Controls.Add(detail, 1, 1);
        if (item.ActionText != null && item.Action != null)
        {
            var b = Btn(item.ActionText);
            var action = item.Action;
            b.Click += (_, _) => { action(); RefreshChecks(); };
            card.Controls.Add(b, 2, 0);
            card.SetRowSpan(b, 2);
        }
        return card;
    }

    // ================================================================== settings

    Panel BuildSettingsPage()
    {
        var col = Column();

        col.Controls.Add(H("OpenRGB"));
        col.Controls.Add(Note("RigGlow uses OpenRGB (free, open source) in the background to talk to RGB hardware. If OpenRGB isn't running, RigGlow starts it for you in server mode.", 640));
        _orgbPath = new TextBox
        {
            Width = 470,
            BackColor = Card,
            ForeColor = Fg,
            BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrWhiteSpace(_s.OpenRgbPath) ? (LightingEngine.FindOpenRgb(null) ?? "") : _s.OpenRgbPath
        };
        _orgbPath.TextChanged += (_, _) => { _s.OpenRgbPath = _orgbPath.Text; MarkDirty(); };
        var browse = Btn("Browse...");
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "OpenRGB|OpenRGB.exe|Programs|*.exe", Title = "Find OpenRGB.exe" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _orgbPath.Text = dlg.FileName;
        };
        col.Controls.Add(Row(Lbl("OpenRGB.exe", width: 100), _orgbPath, browse));
        _autoLaunch = Check("Start OpenRGB automatically when needed", _s.AutoLaunchOpenRgb);
        _autoLaunch.CheckedChanged += (_, _) => { _s.AutoLaunchOpenRgb = _autoLaunch.Checked; MarkDirty(); };
        col.Controls.Add(_autoLaunch);

        col.Controls.Add(H("Startup"));
        _startup = Check("Start RigGlow when I sign in to Windows", Startup.IsEnabled());
        _startup.CheckedChanged += (_, _) =>
        {
            if (!Startup.Set(_startup.Checked, _s.StartMinimized))
                MessageBox.Show(this, "Couldn't change the startup setting.", AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        col.Controls.Add(_startup);
        col.Controls.Add(Note("Uses a Windows scheduled task so RigGlow can start with admin rights (needed for fan control) without a prompt every time.", 640));
        _startMin = Check("When Windows starts RigGlow, keep it hidden in the tray", _s.StartMinimized);
        _startMin.CheckedChanged += (_, _) =>
        {
            _s.StartMinimized = _startMin.Checked;
            MarkDirty();
            if (Startup.IsEnabled()) Startup.Set(true, _s.StartMinimized);
        };
        col.Controls.Add(_startMin);
        _toTray = Check("Closing the window keeps RigGlow running in the tray", _s.MinimizeToTray);
        _toTray.CheckedChanged += (_, _) => { _s.MinimizeToTray = _toTray.Checked; MarkDirty(); };
        col.Controls.Add(_toTray);

        col.Controls.Add(H("Updates & privacy"));
        _updates = Check("Check GitHub for new versions when RigGlow starts", _s.CheckForUpdates);
        _updates.CheckedChanged += (_, _) => { _s.CheckForUpdates = _updates.Checked; MarkDirty(); };
        col.Controls.Add(_updates);
        col.Controls.Add(Note("That's the only time RigGlow goes online. No accounts, no telemetry, no tracking.", 640));

        col.Controls.Add(H("Files"));
        var settingsFolder = Btn("Open settings folder");
        settingsFolder.Click += (_, _) => AppInfo.OpenFolder(AppSettings.Folder);
        var logFolder = Btn("Open log folder");
        logFolder.Click += (_, _) => AppInfo.OpenFolder(Log.Folder);
        col.Controls.Add(Row(settingsFolder, logFolder));
        col.Controls.Add(Note("Settings: " + AppSettings.FilePath, 640));

        return Page("Settings", "How RigGlow starts and where it keeps its files.", col);
    }

    // ================================================================== about

    Panel BuildAboutPage()
    {
        var col = Column();

        var title = new Label { Text = AppInfo.Name, Font = new Font("Segoe UI Semibold", 22f), ForeColor = Fg, AutoSize = true, Margin = new Padding(12, 14, 3, 0) };
        var version = new Label { Text = $"Version {AppInfo.VersionText}", ForeColor = Muted, AutoSize = true, Margin = new Padding(14, 0, 3, 0) };
        var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, BackColor = Bg, WrapContents = false };
        text.Controls.Add(title);
        text.Controls.Add(version);
        col.Controls.Add(Row(new LogoBox(84), text));

        col.Controls.Add(Note(AppInfo.Tagline + ". Made by " + AppInfo.Company + ", free and open source.", 640));
        col.Controls[col.Controls.Count - 1].ForeColor = Fg;

        var site = Btn("Opifex Studios website", primary: true);
        site.Click += (_, _) => AppInfo.OpenUrl(AppInfo.Website);
        var code = Btn("Source code on GitHub");
        code.Click += (_, _) => AppInfo.OpenUrl(AppInfo.RepoUrl);
        var issue = Btn("Report a problem");
        issue.Click += (_, _) => AppInfo.OpenUrl(AppInfo.IssuesUrl);
        col.Controls.Add(Row(site, code, issue));

        col.Controls.Add(H("Updates"));
        var check = Btn("Check for updates");
        _updateInfo = Lbl("", true);
        _updateInfo.Cursor = Cursors.Default;
        _updateInfo.Click += (_, _) => { if (_updateUrl != null) AppInfo.OpenUrl(_updateUrl); };
        check.Click += (_, _) => _ = CheckUpdatesAsync(silent: false, delayMs: 0);
        col.Controls.Add(Row(check, _updateInfo));

        col.Controls.Add(H("Built with"));
        col.Controls.Add(Note("RigGlow stands on the shoulders of these open-source projects. Thank you to everyone who works on them.", 640));
        col.Controls.Add(Row(Link("OpenRGB", AppInfo.OpenRgbUrl), Note("lighting for hundreds of devices (GPL-2.0, runs as a separate program)", 520)));
        col.Controls.Add(Row(Link("LibreHardwareMonitor", "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor"), Note("sensors and fan control (MPL-2.0)", 520)));
        col.Controls.Add(Row(Link("PawnIO", AppInfo.PawnIoUrl), Note("signed hardware-access driver (installed separately)", 520)));
        col.Controls.Add(Row(Link("HidSharp", "https://www.zer7.com/software/hidsharp"), Note("USB HID access (Apache-2.0)", 520)));
        col.Controls.Add(Row(Link(".NET", "https://dotnet.microsoft.com"), Note("runtime (MIT)", 520)));

        col.Controls.Add(H("Licence"));
        col.Controls.Add(Note($"RigGlow is released under the MIT licence. © {DateTime.Now.Year} {AppInfo.Company}. It's provided as-is, without warranty of any kind. Full licence texts are in the install folder (LICENSE.txt and THIRD-PARTY-NOTICES.txt).", 640));

        return Page("About", "Who made this, and what it's built on.", col);
    }

    async Task CheckUpdatesAsync(bool silent, int delayMs)
    {
        if (delayMs > 0) await Task.Delay(delayMs);
        if (!silent && _updateInfo != null) _updateInfo.Text = "Checking...";
        var r = await UpdateChecker.CheckAsync();
        if (IsDisposed) return;
        try
        {
            BeginInvoke(new Action(() =>
            {
                if (r == null)
                {
                    if (!silent) _updateInfo.Text = "Couldn't reach GitHub. Try again later.";
                    return;
                }
                if (r.IsNewer)
                {
                    _updateUrl = r.Url;
                    _updateInfo.Text = $"Version {r.Latest.ToString(3)} is available. Click here to download.";
                    _updateInfo.ForeColor = Accent;
                    _updateInfo.Cursor = Cursors.Hand;
                    _tray.ShowBalloonTip(6000, $"{AppInfo.Name} {r.Latest.ToString(3)} is available",
                        "Click here to see what's new and download it.", ToolTipIcon.Info);
                    Log.Info("Update available: " + r.Latest);
                }
                else if (!silent) _updateInfo.Text = "You're on the latest version.";
            }));
        }
        catch { }
    }

    // ================================================================== tray & window

    ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open " + AppInfo.Name, null, (_, _) => ShowFromTray());
        var fx = new ToolStripMenuItem("Effect");
        for (int i = 0; i < Effects.Choices.Length; i++)
        {
            int idx = i;
            fx.DropDownItems.Add(Effects.Choices[i].Name, null, (_, _) =>
            {
                _s.Effect = Effects.Choices[idx].Type;
                _effect.SelectedIndex = idx;
                MarkDirty();
            });
        }
        menu.Items.Add(fx);
        menu.Items.Add("Setup check", null, (_, _) => { ShowFromTray(); ShowPage("Setup check"); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit (fans go back to BIOS control)", null, (_, _) => ExitApp());
        return menu;
    }

    public void ShowFromTray()
    {
        _allowShow = true;
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    public void ExitApp()
    {
        _exiting = true;
        _tray.Visible = false;
        _s.Save();
        Application.Exit();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UseDarkTitleBar(Handle);
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!_allowShow) value = false;
        base.SetVisibleCore(value);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && _s.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!_s.TrayHintShown)
            {
                _s.TrayHintShown = true;
                MarkDirty();
                _tray.ShowBalloonTip(4000, $"{AppInfo.Name} is still running",
                    "Your lighting and fan curves keep working. Right-click the tray icon to exit.", ToolTipIcon.Info);
            }
            return;
        }
        _tray.Visible = false;
        _uiTimer.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray?.Dispose();
            _uiTimer?.Dispose();
            _saveTimer?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ================================================================== periodic refresh

    void RefreshUi()
    {
        bool lit = _engine.IsConnected;
        _rgbStatus.Text = _engine.Status;
        _rgbStatus.ForeColor = lit ? Good : Warn;
        _lightDot.DotColor = lit ? Good : Warn;
        _lightState.Text = lit ? $"Lighting: {_engine.Devices.Count} device(s)" : "Lighting: not connected";

        if (_hw.Ready && !_fansPopulated)
        {
            _fansPopulated = true;
            PopulateFans();
        }

        if (!_hw.Ready)
        {
            _hwStatus.Text = "Reading sensors...";
            _fanDot.DotColor = Accent;
            _fanState.Text = "Fans: starting";
        }
        else if (!string.IsNullOrEmpty(_hw.Error))
        {
            _hwStatus.Text = _hw.Error;
            _hwStatus.ForeColor = Warn;
            _fanDot.DotColor = Bad;
            _fanState.Text = "Fans: unavailable";
        }
        else
        {
            var cpu = _hw.GetTemp(HardwareMonitor.AutoCpu);
            var gpu = _hw.GetTemp(HardwareMonitor.AutoGpu);
            _hwStatus.Text = $"CPU {(cpu.HasValue ? cpu.Value.ToString("0") + "°C" : "-")}   ·   GPU {(gpu.HasValue ? gpu.Value.ToString("0") + "°C" : "-")}";
            _hwStatus.ForeColor = _hw.SafetyOverride ? Warn : Muted;
            int controlled = _hw.Fans.Count(f => f.UnderOurControl);
            _fanDot.DotColor = _hw.SafetyOverride ? Warn : Good;
            _fanState.Text = controlled > 0 ? $"Fans: {controlled} controlled" : "Fans: BIOS control";
        }

        if (Visible)
        {
            if (_current == "Fans") { _fanList.Invalidate(); UpdateFanNow(); }
            if (_current == "Setup check" && (DateTime.Now - _lastCheck).TotalSeconds >= 4) RefreshChecks();
        }
        var tip = $"{AppInfo.Name} - " + (lit ? "lighting connected" : "waiting for OpenRGB");
        _tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
    }
}

/// <summary>Start-with-Windows via a scheduled task that runs with highest privileges.</summary>
static class Startup
{
    const string TaskName = "RigGlow";

    public static bool IsEnabled()
    {
        try { return Run($"/Query /TN \"{TaskName}\"") == 0; }
        catch { return false; }
    }

    public static bool Set(bool on, bool minimized = true)
    {
        try
        {
            if (!on) return Run($"/Delete /F /TN \"{TaskName}\"") == 0;
            var exe = Environment.ProcessPath;
            var arg = minimized ? " --minimized" : "";
            return Run($"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /DELAY 0000:15 /TR \"\\\"{exe}\\\"{arg}\"") == 0;
        }
        catch { return false; }
    }

    static int Run(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        if (p == null) return -1;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        return p.ExitCode;
    }
}
