using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CloveParentalGuard.Agent;

/// <summary>
/// Builds the tray icon. Instead of <see cref="SystemIcons.Shield"/> we draw the
/// game-controller glyph from "Segoe Fluent Icons" / "Segoe MDL2 Assets" (shipped
/// with Windows 10/11) so there is no binary asset to ship.
/// </summary>
internal static class TrayIcon
{
    private const string ControllerGlyph = ""; // "Game" controller glyph
    private static readonly Color Accent = Color.FromArgb(124, 92, 255); // indigo — reads on light + dark taskbars

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon BuildController()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            using var font = PickGlyphFont(22f);
            using var brush = new SolidBrush(Accent);
            using var fmt = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString(ControllerGlyph, font, brush, new RectangleF(0, 0, 32, 32), fmt);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            // Clone so the managed Icon owns its own copy and we can free the GDI handle.
            using var fromHandle = Icon.FromHandle(hIcon);
            return (Icon)fromHandle.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Font PickGlyphFont(float emPixels)
    {
        foreach (var family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                var font = new Font(family, emPixels, FontStyle.Regular, GraphicsUnit.Pixel);
                if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
                    return font;
                font.Dispose();
            }
            catch { /* family unavailable — try the next */ }
        }
        // Last resort: a normal font (renders a box, but never throws).
        return new Font(SystemFonts.IconTitleFont?.FontFamily ?? FontFamily.GenericSansSerif,
            emPixels, FontStyle.Regular, GraphicsUnit.Pixel);
    }
}
