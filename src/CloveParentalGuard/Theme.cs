using System.Drawing.Drawing2D;

namespace CloveParentalGuard.App;

/// <summary>Shared palette, type, and drawing helpers for the modern UI pass.</summary>
static class Theme
{
    public static readonly Color WindowBg   = Color.FromArgb(0xF5, 0xF5, 0xF7);
    public static readonly Color Surface    = Color.White;
    public static readonly Color Text       = Color.FromArgb(0x1D, 0x1D, 0x1F);
    public static readonly Color Secondary  = Color.FromArgb(0x6E, 0x6E, 0x73);
    public static readonly Color Accent     = Color.FromArgb(0x0A, 0x84, 0xFF);
    public static readonly Color AccentDown = Color.FromArgb(0x0A, 0x6F, 0xD8);
    public static readonly Color AccentSoft = Color.FromArgb(0xB3, 0xD4, 0xFB); // disabled accent
    public static readonly Color Danger     = Color.FromArgb(0xFF, 0x3B, 0x30);
    public static readonly Color Success    = Color.FromArgb(0x34, 0xC7, 0x59);
    public static readonly Color Field      = Color.FromArgb(0xEC, 0xEC, 0xEF);
    public static readonly Color FieldDown  = Color.FromArgb(0xDF, 0xDF, 0xE4);
    public static readonly Color Hairline   = Color.FromArgb(0xE2, 0xE2, 0xE6);

    private static string? _family;
    public static string Family => _family ??= Pick("Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI");

    public static Font Title()   => new(Family, 18f, FontStyle.Bold);
    public static Font Heading() => new(Family, 12.5f, FontStyle.Bold);
    public static Font Body()    => new(Family, 10.5f, FontStyle.Regular);
    public static Font Caption() => new(Family, 9f, FontStyle.Regular);

    /// <summary>The Segoe glyph font (controller / lock / check icons drawn as text).</summary>
    public static Font Glyph(float px) =>
        new(Pick("Segoe Fluent Icons", "Segoe MDL2 Assets", Family), px, FontStyle.Regular, GraphicsUnit.Pixel);

    public const string GlyphController = ""; // Game
    public const string GlyphLock       = ""; // Lock

    public static void StyleForm(Form f)
    {
        f.AutoScaleMode = AutoScaleMode.Font; // robust with AutoSize layouts across DPIs
        f.BackColor = WindowBg;
        f.ForeColor = Text;
        f.Font = Body();
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = false;
        f.MinimizeBox = false;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.ShowIcon = false;
    }

    /// <summary>A left label used above inputs (e.g. "PARENT CODE").</summary>
    public static Label FieldCaption(string text) => new()
    {
        AutoSize = true,
        Font = Caption(),
        ForeColor = Secondary,
        Text = text,
        Margin = new Padding(2, 2, 0, 4),
    };

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0f) { path.AddRectangle(r); return path; }
        float d = radius * 2f;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string Pick(params string[] families)
    {
        foreach (var f in families)
        {
            try
            {
                using var probe = new Font(f, 9f);
                if (string.Equals(probe.Name, f, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch { /* not installed — try next */ }
        }
        return families[^1];
    }
}
