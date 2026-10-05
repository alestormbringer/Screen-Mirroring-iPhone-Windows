using System.Drawing.Drawing2D;
using IPhoneMirror.HidProbe.Hid;

namespace IPhoneMirror.HidProbe.UI;

/// <summary>
/// Input surface for the probe. In relative mode, mouse movement inside the panel is turned
/// into dx/dy; in absolute mode the panel contains an iPhone-shaped rectangle and the cursor
/// position inside it is mapped to absolute X/Y. Clicking the panel gives it keyboard focus.
/// </summary>
internal sealed class TouchpadPanel : Control
{
    // iPhone 16 Pro Max screen size in points (portrait).
    private const float PhoneWidth = 440f;
    private const float PhoneHeight = 956f;

    private Point? _lastPosition;
    private double _remainderX;
    private double _remainderY;
    private int _wheelAccumulator;
    private PointF? _absolutePosition;
    private PointerMode _mode;

    public TouchpadPanel()
    {
        SetStyle(
            ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw,
            true);
        TabStop = true;
        BackColor = Color.FromArgb(30, 30, 34);
        ForeColor = Color.Gainsboro;
        Cursor = Cursors.Cross;
    }

    /// <summary>Relative movement in report units (already multiplied by <see cref="Sensitivity"/>).</summary>
    public event Action<int, int>? RelativeMoved;

    /// <summary>Absolute position, normalized to 0..1 on both axes.</summary>
    public event Action<double, double>? AbsoluteMoved;

    /// <summary>HID button bit (1=left, 2=right, 4=middle) and whether it is now pressed.</summary>
    public event Action<byte, bool>? ButtonChanged;

    /// <summary>Wheel notches, positive = away from the user (scroll up).</summary>
    public event Action<int>? WheelScrolled;

    public PointerMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _absolutePosition = null;
            Invalidate();
        }
    }

    public double Sensitivity { get; set; } = 1.0;

    private RectangleF PhoneRect
    {
        get
        {
            var area = RectangleF.Inflate(ClientRectangle, -16, -40);
            var scale = Math.Min(area.Width / PhoneWidth, area.Height / PhoneHeight);
            var w = PhoneWidth * scale;
            var h = PhoneHeight * scale;
            return new RectangleF(area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
        }
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _lastPosition = PointToClient(MousePosition);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _lastPosition = null;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Focused)
        {
            Focus();
        }

        if (_mode == PointerMode.Absolute)
        {
            EmitAbsolute(e.Location);
        }

        var bit = ButtonBit(e.Button);
        if (bit != 0)
        {
            ButtonChanged?.Invoke(bit, true);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var bit = ButtonBit(e.Button);
        if (bit != 0)
        {
            ButtonChanged?.Invoke(bit, false);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_mode == PointerMode.Absolute)
        {
            EmitAbsolute(e.Location);
            return;
        }

        if (_lastPosition is { } last)
        {
            var fx = ((e.X - last.X) * Sensitivity) + _remainderX;
            var fy = ((e.Y - last.Y) * Sensitivity) + _remainderY;
            var dx = (int)Math.Truncate(fx);
            var dy = (int)Math.Truncate(fy);
            _remainderX = fx - dx;
            _remainderY = fy - dy;
            if (dx != 0 || dy != 0)
            {
                RelativeMoved?.Invoke(dx, dy);
            }
        }

        _lastPosition = e.Location;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _wheelAccumulator += e.Delta;
        var notches = _wheelAccumulator / 120;
        if (notches != 0)
        {
            _wheelAccumulator -= notches * 120;
            WheelScrolled?.Invoke(notches);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        using var font = new Font(Font.FontFamily, 9.5f);
        using var textBrush = new SolidBrush(ForeColor);
        var header = _mode == PointerMode.Absolute
            ? "Modalità ASSOLUTA: il rettangolo rappresenta lo schermo dell'iPhone"
            : "Modalità RELATIVA: muovi il mouse qui dentro (esci e rientra per \"sollevare\")";
        g.DrawString(header, font, textBrush, new RectangleF(10, 8, Width - 20, 32));

        if (_mode == PointerMode.Absolute)
        {
            var phone = PhoneRect;
            using var phonePen = new Pen(Color.FromArgb(120, 160, 255), 2);
            using var path = RoundedRect(phone, Math.Min(phone.Width, phone.Height) * 0.12f);
            g.DrawPath(phonePen, path);
            if (_absolutePosition is { } p)
            {
                var x = phone.X + (p.X * phone.Width);
                var y = phone.Y + (p.Y * phone.Height);
                using var crossPen = new Pen(Color.Orange, 1.5f);
                g.DrawLine(crossPen, x - 10, y, x + 10, y);
                g.DrawLine(crossPen, x, y - 10, x, y + 10);
            }
        }

        var footer = Focused
            ? "TASTIERA → iPhone attiva (clicca fuori dal riquadro per fermarla)"
            : "Clicca nel riquadro per inoltrare la tastiera all'iPhone";
        using var footerBrush = new SolidBrush(Focused ? Color.LightGreen : Color.Silver);
        g.DrawString(footer, font, footerBrush, new RectangleF(10, Height - 26, Width - 20, 20));

        using var borderPen = new Pen(Focused ? Color.LightGreen : Color.DimGray, Focused ? 3 : 1);
        g.DrawRectangle(borderPen, 1, 1, Width - 3, Height - 3);
    }

    private void EmitAbsolute(Point location)
    {
        var phone = PhoneRect;
        if (phone.Width <= 0 || phone.Height <= 0)
        {
            return;
        }

        var nx = Math.Clamp((location.X - phone.X) / phone.Width, 0f, 1f);
        var ny = Math.Clamp((location.Y - phone.Y) / phone.Height, 0f, 1f);
        _absolutePosition = new PointF(nx, ny);
        AbsoluteMoved?.Invoke(nx, ny);
        Invalidate();
    }

    private static byte ButtonBit(MouseButtons button) => button switch
    {
        MouseButtons.Left => 0x01,
        MouseButtons.Right => 0x02,
        MouseButtons.Middle => 0x04,
        _ => 0,
    };

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
