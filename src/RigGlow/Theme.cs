using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RigGlow;

/// <summary>Colors and small control factories so every screen looks the same.</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(22, 24, 30);
    public static readonly Color Sidebar = Color.FromArgb(16, 17, 22);
    public static readonly Color Card = Color.FromArgb(32, 35, 43);
    public static readonly Color CardHover = Color.FromArgb(40, 44, 54);
    public static readonly Color Border = Color.FromArgb(58, 62, 76);
    public static readonly Color Fg = Color.FromArgb(234, 236, 242);
    public static readonly Color Muted = Color.FromArgb(150, 155, 172);
    public static readonly Color Accent = Color.FromArgb(0, 186, 255);
    public static readonly Color Accent2 = Color.FromArgb(176, 64, 255);
    public static readonly Color Good = Color.FromArgb(80, 212, 120);
    public static readonly Color Warn = Color.FromArgb(255, 176, 64);
    public static readonly Color Bad = Color.FromArgb(255, 92, 92);

    public static readonly Font Body = new Font("Segoe UI", 9.5f);
    public static readonly Font Small = new Font("Segoe UI", 8.5f);
    public static readonly Font Heading = new Font("Segoe UI Semibold", 12f);
    public static readonly Font PageTitle = new Font("Segoe UI Semibold", 18f);
    public static readonly Font Strong = new Font("Segoe UI Semibold", 10f);

    public static Color StateColor(CheckStatus s) => s switch
    {
        CheckStatus.Ok => Good,
        CheckStatus.Warning => Warn,
        CheckStatus.Problem => Bad,
        CheckStatus.Waiting => Accent,
        _ => Muted
    };

    // ---- control factories ----

    public static Label Lbl(string text, bool muted = false, int width = 0)
    {
        var l = new Label
        {
            Text = text,
            AutoSize = width == 0,
            ForeColor = muted ? Muted : Fg,
            Margin = new Padding(3, 7, 6, 3)
        };
        if (width > 0) { l.Width = width; l.Height = 22; }
        return l;
    }

    public static Label Note(string text, int width = 460) => new Label
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Margin = new Padding(3, 2, 3, 8)
    };

    public static Label H(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        ForeColor = Fg,
        Font = Heading,
        Margin = new Padding(2, 16, 2, 6)
    };

    public static FlowLayoutPanel Row(params Control[] items)
    {
        var p = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 1, 0, 1),
            BackColor = Color.Transparent
        };
        p.Controls.AddRange(items);
        return p;
    }

    public static FlowLayoutPanel Column() => new FlowLayoutPanel
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Dock = DockStyle.Fill,
        Padding = new Padding(4, 0, 14, 14),
        BackColor = Bg
    };

    public static ComboBox Combo(int width) => new ComboBox
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = Card,
        ForeColor = Fg,
        Width = width
    };

    public static Button Btn(string text, int width = 0, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(0, 120, 170) : Card,
            ForeColor = Fg,
            AutoSize = width == 0,
            Height = 32,
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(3, 3, 8, 3),
            Cursor = Cursors.Hand
        };
        if (width > 0) b.Width = width;
        b.FlatAppearance.BorderColor = primary ? Accent : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(0, 140, 196) : CardHover;
        return b;
    }

    public static NumericUpDown Num(int min, int max, int value, int width = 70) => new NumericUpDown
    {
        Minimum = min,
        Maximum = max,
        Value = Math.Clamp(value, min, max),
        Width = width,
        BackColor = Card,
        ForeColor = Fg,
        BorderStyle = BorderStyle.FixedSingle
    };

    public static TrackBar Slider(int value, int width = 300, int min = 0) => new TrackBar
    {
        Minimum = min,
        Maximum = 100,
        TickFrequency = 10,
        Value = Math.Clamp(value, min, 100),
        Width = width,
        BackColor = Bg,
        AutoSize = false,
        Height = 34
    };

    public static CheckBox Check(string text, bool value) => new CheckBox
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = Fg,
        Margin = new Padding(3, 6, 3, 3)
    };

    public static LinkLabel Link(string text, string url)
    {
        var l = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Accent,
            ActiveLinkColor = Color.White,
            VisitedLinkColor = Accent,
            Margin = new Padding(3, 3, 12, 3)
        };
        l.LinkClicked += (_, _) => AppInfo.OpenUrl(url);
        return l;
    }

    /// <summary>The RigGlow ring logo, drawn at any size.</summary>
    public static void DrawLogo(Graphics g, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new LinearGradientBrush(r, Accent, Accent2, 45f);
        float ring = r.Width * 0.2f;
        using var pen = new Pen(brush, ring);
        var inner = RectangleF.Inflate(r, -ring / 2f, -ring / 2f);
        g.DrawEllipse(pen, inner);
        using var glow = new Pen(Color.FromArgb(60, Accent), ring * 1.6f);
        g.DrawArc(glow, inner, 200, 80);
    }

    // ---- dark title bar (Windows 10 1809+ / Windows 11) ----

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void UseDarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));
        }
        catch { }
    }
}

/// <summary>Sidebar navigation entry.</summary>
public sealed class NavButton : Control
{
    static readonly Font GlyphFont = new Font("Segoe UI Symbol", 11f);
    bool _active, _hover;
    public string Glyph;

    public NavButton(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        Height = 42;
        Width = 200;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 1, 0, 1);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Font = new Font("Segoe UI Semibold", 10f);
    }

    public bool Active { get => _active; set { _active = value; Invalidate(); } }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var bg = _active ? Theme.Card : _hover ? Color.FromArgb(26, 28, 35) : Theme.Sidebar;
        using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
        if (_active)
        {
            using var a = new LinearGradientBrush(new Rectangle(0, 0, 4, Height), Theme.Accent, Theme.Accent2, 90f);
            g.FillRectangle(a, 0, 6, 4, Height - 12);
        }
        var color = _active ? Theme.Fg : Theme.Muted;
        TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle(16, 0, 26, Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(48, 0, Width - 52, Height), color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

/// <summary>A small coloured status dot.</summary>
public sealed class Dot : Control
{
    Color _color = Theme.Muted;
    public Dot()
    {
        Size = new Size(12, 12);
        Margin = new Padding(3, 9, 4, 3);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    public Color DotColor { get => _color; set { _color = value; Invalidate(); } }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var b = new SolidBrush(_color);
        e.Graphics.FillEllipse(b, 1, 1, Width - 3, Height - 3);
    }
}

/// <summary>Draws the logo; used in the sidebar and About page.</summary>
public sealed class LogoBox : Control
{
    public LogoBox(int size)
    {
        Size = new Size(size, size);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e) =>
        Theme.DrawLogo(e.Graphics, new RectangleF(2, 2, Width - 5, Height - 5));
}
