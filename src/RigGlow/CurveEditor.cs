using System.Drawing.Drawing2D;

namespace RigGlow;

/// <summary>Draggable fan curve: temperature (°C) across, fan speed (%) up.</summary>
public sealed class CurveEditor : Control
{
    const float TMin = 20f, TMax = 100f;

    List<CurvePoint> _points = new();
    int _drag = -1;
    int _hover = -1;

    public float? CurrentTemp;
    public float? CurrentPercent;
    public event Action Changed;

    static readonly Color GridColor = Color.FromArgb(52, 56, 68);
    static readonly Color TextColor = Color.FromArgb(150, 155, 170);
    static readonly Color LineColor = Color.FromArgb(0, 174, 239);
    static readonly Color NowColor = Color.FromArgb(255, 150, 40);

    public CurveEditor()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(34, 37, 45);
        Font = new Font("Segoe UI", 8.5f);
    }

    public List<CurvePoint> Points
    {
        get => _points;
        set
        {
            _points = value ?? new List<CurvePoint>();
            _points.Sort((a, b) => a.T.CompareTo(b.T));
            Invalidate();
        }
    }

    Rectangle Plot => new Rectangle(46, 12, Math.Max(10, Width - 62), Math.Max(10, Height - 42));

    PointF ToScreen(float t, float p)
    {
        var r = Plot;
        return new PointF(r.Left + (t - TMin) / (TMax - TMin) * r.Width, r.Bottom - p / 100f * r.Height);
    }

    (float t, float p) FromScreen(Point pt)
    {
        var r = Plot;
        float t = TMin + (pt.X - r.Left) / (float)r.Width * (TMax - TMin);
        float p = (r.Bottom - pt.Y) / (float)r.Height * 100f;
        return (Math.Clamp(t, TMin, TMax), Math.Clamp(p, 0f, 100f));
    }

    int HitTest(Point pt)
    {
        for (int i = 0; i < _points.Count; i++)
        {
            var s = ToScreen(_points[i].T, _points[i].P);
            if (Math.Abs(s.X - pt.X) <= 9 && Math.Abs(s.Y - pt.Y) <= 9) return i;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = Plot;

        using var gridPen = new Pen(GridColor);
        using var textBrush = new SolidBrush(TextColor);

        for (int t = 20; t <= 100; t += 10)
        {
            var x = ToScreen(t, 0).X;
            g.DrawLine(gridPen, x, r.Top, x, r.Bottom);
            var label = t + "°";
            var sz = g.MeasureString(label, Font);
            g.DrawString(label, Font, textBrush, x - sz.Width / 2, r.Bottom + 4);
        }
        for (int p = 0; p <= 100; p += 20)
        {
            var y = ToScreen(TMin, p).Y;
            g.DrawLine(gridPen, r.Left, y, r.Right, y);
            var label = p + "%";
            var sz = g.MeasureString(label, Font);
            g.DrawString(label, Font, textBrush, r.Left - sz.Width - 4, y - sz.Height / 2);
        }

        if (CurrentTemp.HasValue)
        {
            float ct = Math.Clamp(CurrentTemp.Value, TMin, TMax);
            var x = ToScreen(ct, 0).X;
            using var nowPen = new Pen(NowColor, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawLine(nowPen, x, r.Top, x, r.Bottom);
            using var nowBrush = new SolidBrush(NowColor);
            g.DrawString($"{CurrentTemp.Value:0}°C", Font, nowBrush, x + 3, r.Top);
            if (CurrentPercent.HasValue)
            {
                var pt = ToScreen(ct, Math.Clamp(CurrentPercent.Value, 0, 100));
                g.FillEllipse(nowBrush, pt.X - 4, pt.Y - 4, 8, 8);
            }
        }

        if (_points.Count == 0) return;

        var pts = _points.Select(p => ToScreen(p.T, p.P)).ToList();
        var line = new List<PointF> { new PointF(r.Left, pts[0].Y) };
        line.AddRange(pts);
        line.Add(new PointF(r.Right, pts[^1].Y));

        using (var fill = new GraphicsPath())
        {
            fill.AddLines(line.ToArray());
            fill.AddLine(r.Right, pts[^1].Y, r.Right, r.Bottom);
            fill.AddLine(r.Right, r.Bottom, r.Left, r.Bottom);
            fill.CloseFigure();
            using var fb = new SolidBrush(Color.FromArgb(40, LineColor));
            g.FillPath(fb, fill);
        }

        using (var pen = new Pen(LineColor, 2.2f))
            g.DrawLines(pen, line.ToArray());

        for (int i = 0; i < pts.Count; i++)
        {
            bool active = i == _drag || i == _hover;
            float rad = active ? 7 : 5.5f;
            using var b = new SolidBrush(active ? Color.White : LineColor);
            g.FillEllipse(b, pts[i].X - rad, pts[i].Y - rad, rad * 2, rad * 2);
            if (active)
            {
                var label = $"{_points[i].T:0}°C  →  {_points[i].P:0}%";
                using var wb = new SolidBrush(Color.White);
                g.DrawString(label, Font, wb, Math.Min(pts[i].X + 8, r.Right - 90), Math.Max(pts[i].Y - 20, r.Top));
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _drag = HitTest(e.Location);
        if (_drag >= 0) Capture = true;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag >= 0)
        {
            var (t, p) = FromScreen(e.Location);
            float lo = _drag > 0 ? _points[_drag - 1].T + 1 : TMin;
            float hi = _drag < _points.Count - 1 ? _points[_drag + 1].T - 1 : TMax;
            _points[_drag].T = (float)Math.Round(Math.Clamp(t, lo, Math.Max(lo, hi)));
            _points[_drag].P = (float)Math.Round(p);
            Invalidate();
            Changed?.Invoke();
        }
        else
        {
            int h = HitTest(e.Location);
            if (h != _hover) { _hover = h; Invalidate(); }
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = -1;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }
}
