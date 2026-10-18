using System.Drawing.Drawing2D;

namespace CloveParentalGuard.App;

/// <summary>Flat, rounded, custom-painted button. Primary = filled accent, else subtle gray.</summary>
class PillButton : Button
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Primary { get; set; } = true;
    private bool _hover, _down;

    public PillButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Font = Theme.Body();
        Height = 42;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(r, 10f);

        Color fill, fg;
        if (Primary)
        {
            fill = !Enabled ? Theme.AccentSoft : _down ? Theme.AccentDown : _hover ? Theme.AccentDown : Theme.Accent;
            fg = Color.White;
        }
        else
        {
            fill = _down ? Theme.FieldDown : _hover ? Theme.FieldDown : Theme.Field;
            fg = Enabled ? Theme.Text : Theme.Secondary;
        }

        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A rounded surface — used as a card and as the background of inputs.</summary>
class RoundedPanel : Panel
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public float Radius { get; set; } = 12f;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Color Fill { get; set; } = Theme.Surface;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public Color Stroke { get; set; } = Color.Transparent;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedRect(r, Radius);
        using (var b = new SolidBrush(Fill)) g.FillPath(b, path);
        if (Stroke != Color.Transparent)
        {
            using var pen = new Pen(Stroke, 1f);
            g.DrawPath(pen, path);
        }
    }
}

/// <summary>A rounded text field hosting a borderless, vertically-centered TextBox.</summary>
class RoundedInput : RoundedPanel
{
    public TextBox Box { get; }

    public RoundedInput(string placeholder, bool password = false)
    {
        Box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Field,
            ForeColor = Theme.Text,
            Font = Theme.Body(),
            PlaceholderText = placeholder,
            UseSystemPasswordChar = password,
        };
        Radius = 10f;
        Fill = Theme.Field;
        Height = 46;              // triggers OnSizeChanged -> LayoutBox (Box now exists)
        Controls.Add(Box);
        LayoutBox();
    }

    public new string Text => Box.Text;
    public void Clear() => Box.Clear();

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); LayoutBox(); }

    private void LayoutBox()
    {
        if (Box is null) return; // guard: Height set during base ctor before Box assigned
        const int pad = 16;
        Box.Left = pad;
        Box.Width = Math.Max(0, Width - pad * 2);
        Box.Top = Math.Max(0, (Height - Box.PreferredHeight) / 2);
    }
}

/// <summary>A filled green circle with a white checkmark — the install/unlock success badge.</summary>
class CheckBadge : Control
{
    public CheckBadge()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(60, 60);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
        using (var b = new SolidBrush(Theme.Success)) g.FillEllipse(b, r);

        using var pen = new Pen(Color.White, Math.Max(2.5f, Width * 0.08f))
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        float w = Width, h = Height;
        g.DrawLines(pen, new[]
        {
            new PointF(w * 0.30f, h * 0.52f),
            new PointF(w * 0.44f, h * 0.66f),
            new PointF(w * 0.72f, h * 0.36f),
        });
    }
}

/// <summary>A row of segmented duration pills with single-selection.</summary>
class DurationPicker : FlowLayoutPanel
{
    private readonly List<Pill> _pills = new();

    public DurationPicker()
    {
        AutoSize = true;
        WrapContents = true;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
    }

    public int? SelectedMinutes => _pills.FirstOrDefault(p => p.Selected)?.Minutes;

    public void SetDurations(IEnumerable<int> minutes)
    {
        Controls.Clear();
        _pills.Clear();
        foreach (var m in minutes)
        {
            var pill = new Pill(m);
            pill.Click += (_, _) => Select(pill);
            _pills.Add(pill);
            Controls.Add(pill);
        }
        if (_pills.Count > 0) Select(_pills[0]);
    }

    private void Select(Pill chosen)
    {
        foreach (var p in _pills) p.Selected = ReferenceEquals(p, chosen);
    }

    private static string Label(int m) => m % 60 == 0 ? $"{m / 60} h" : $"{m} min";

    private sealed class Pill : Control
    {
        public int Minutes { get; }
        private bool _selected;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Selected { get => _selected; set { _selected = value; Invalidate(); } }

        public Pill(int minutes)
        {
            Minutes = minutes;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Body();
            Cursor = Cursors.Hand;
            Text = Label(minutes);

            // Size to the label plus generous horizontal padding so the text isn't cramped.
            var textWidth = TextRenderer.MeasureText(Text, Font).Width;
            Size = new Size(Math.Max(60, textWidth + 34), 44);
            Margin = new Padding(0, 0, 10, 10);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using var path = Theme.RoundedRect(r, 9f);
            using (var b = new SolidBrush(_selected ? Theme.Accent : Theme.Field)) g.FillPath(b, path);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height),
                _selected ? Color.White : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
