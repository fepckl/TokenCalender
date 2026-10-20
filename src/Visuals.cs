using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ClaudeUsageTracker
{
    class Palette
    {
        public Color Background, Surface, Text, SubText, Track, Border, Accent;

        public static readonly Color Safe = Color.FromArgb(52, 168, 83);
        public static readonly Color Moderate = Color.FromArgb(230, 145, 30);
        public static readonly Color Critical = Color.FromArgb(220, 60, 50);
        public static readonly Color ClaudeOrange = Color.FromArgb(217, 119, 87);

        public static Color ForStatus(StatusLevel s)
        {
            return s == StatusLevel.Safe ? Safe : s == StatusLevel.Moderate ? Moderate : Critical;
        }

        public static Palette Current()
        {
            if (WindowsTheme.IsDark)
                return new Palette
                {
                    Background = Color.FromArgb(32, 32, 32),
                    Surface = Color.FromArgb(44, 44, 44),
                    Text = Color.FromArgb(240, 240, 240),
                    SubText = Color.FromArgb(160, 160, 160),
                    Track = Color.FromArgb(62, 62, 62),
                    Border = Color.FromArgb(70, 70, 70),
                    Accent = ClaudeOrange
                };
            return new Palette
            {
                Background = Color.FromArgb(249, 249, 249),
                Surface = Color.White,
                Text = Color.FromArgb(28, 28, 28),
                SubText = Color.FromArgb(105, 105, 105),
                Track = Color.FromArgb(226, 226, 226),
                Border = Color.FromArgb(215, 215, 215),
                Accent = ClaudeOrange
            };
        }
    }

    /// Draws the tray icon: a rounded tile in the status color with the percentage on it.
    static class TrayIconRenderer
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        public static Icon Render(string text, Color fill, int size)
        {
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                var r = size / 5f;
                using (var path = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), r))
                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);

                // Shrink the font until the text fits (e.g. "100" or "!").
                float fontSize = size * (text.Length >= 3 ? 0.42f : 0.62f);
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    Font font = null;
                    try
                    {
                        while (true)
                        {
                            if (font != null) font.Dispose();
                            font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                            var m = g.MeasureString(text, font);
                            if (m.Width <= size * 1.08f || fontSize < 6) break;
                            fontSize -= 1;
                        }
                        g.DrawString(text, font, Brushes.White, new RectangleF(0, size * 0.03f, size, size), sf);
                    }
                    finally { if (font != null) font.Dispose(); }
                }

                // Icon.FromHandle does not own the HICON; clone it so we can free the handle now.
                var h = bmp.GetHicon();
                try { using (var tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class Format
    {
        public static string Countdown(DateTime? resetUtc)
        {
            if (!resetUtc.HasValue) return "";
            var left = resetUtc.Value - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return L.T("gerade zurückgesetzt");
            string span;
            if (left.TotalDays >= 1) span = string.Format("{0} d {1} h", (int)left.TotalDays, left.Hours);
            else if (left.TotalHours >= 1) span = string.Format("{0} h {1} min", (int)left.TotalHours, left.Minutes);
            else span = string.Format("{0} min", Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)));
            return L.T("Reset in ") + span;
        }

        public static string ResetClock(DateTime? resetUtc)
        {
            if (!resetUtc.HasValue) return "";
            var local = resetUtc.Value.ToLocalTime();
            // "t" is only the short-time pattern when it stands alone; inside "ddd t" it means AM/PM.
            var time = local.ToString("t");
            return local.Date == DateTime.Today ? time : local.ToString("ddd") + " " + time;
        }

        public static string Percent(double p)
        {
            return Math.Round(p).ToString("0") + " %";
        }
    }
}
