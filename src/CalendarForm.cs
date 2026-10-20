using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClaudeUsageTracker
{
    /// Month calendar heatmap of tokens per day from local Claude Code transcripts.
    class CalendarForm : Form
    {
        TokenHistoryResult data;
        string error;
        bool loading;
        DateTime month;              // first day of the shown month
        DateTime selected;
        bool includeCacheReads = true;
        Palette pal;
        float scale = 1f;

        // Hit-test targets, rebuilt on every paint
        Rectangle prevBtn, nextBtn, todayBtn, refreshBtn, toggleAll, toggleNoCache;
        readonly Dictionary<DateTime, Rectangle> cells = new Dictionary<DateTime, Rectangle>();

        public CalendarForm()
        {
            Text = L.T("Claude Token-Kalender");
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            ResizeRedraw = true;
            month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            selected = DateTime.Today;
            pal = Palette.Current();
            BackColor = pal.Background;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            ClientSize = new Size(S(900), S(620));
            MinimumSize = new Size(S(720), S(520));
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Reload();
        }

        async void Reload()
        {
            if (loading) return;
            loading = true;
            error = null;
            Invalidate();
            try { data = await Task.Run(() => TokenHistory.Load()); }
            catch (Exception ex) { error = ex.Message; }
            loading = false;
            Invalidate();
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        DayUsage Day(DateTime d)
        {
            DayUsage u;
            return data != null && data.Days.TryGetValue(d.Date, out u) ? u : null;
        }

        long Value(DateTime d)
        {
            var u = Day(d);
            return u == null ? 0 : u.Total(includeCacheReads);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(pal.Background);
            cells.Clear();

            int pad = S(20);
            int detailW = S(270);
            int gridW = ClientSize.Width - 3 * pad - detailW;

            using (var h1 = new Font("Segoe UI Semibold", 20f * scale, GraphicsUnit.Pixel))
            using (var h2 = new Font("Segoe UI Semibold", 14f * scale, GraphicsUnit.Pixel))
            using (var body = new Font("Segoe UI", 12.5f * scale, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 11f * scale, GraphicsUnit.Pixel))
            using (var text = new SolidBrush(pal.Text))
            using (var sub = new SolidBrush(pal.SubText))
            using (var accent = new SolidBrush(pal.Accent))
            {
                // ── Header: ‹ Month Year ›  [Heute]          [Alle Tokens | Ohne Cache-Lesen] [↻]
                int y = pad;
                prevBtn = new Rectangle(pad, y, S(32), S(32));
                DrawButton(g, prevBtn, "‹", h2, false);
                var title = month.ToString("MMMM yyyy");
                var tsz = g.MeasureString(title, h1);
                g.DrawString(title, h1, text, prevBtn.Right + S(8), y);
                nextBtn = new Rectangle((int)(prevBtn.Right + S(16) + tsz.Width), y, S(32), S(32));
                DrawButton(g, nextBtn, "›", h2, false);
                todayBtn = new Rectangle(nextBtn.Right + S(10), y + S(2), S(70), S(28));
                DrawButton(g, todayBtn, L.T("Heute"), small, false);

                refreshBtn = new Rectangle(ClientSize.Width - pad - S(32), y, S(32), S(32));
                DrawButton(g, refreshBtn, "↻", h2, false);
                toggleNoCache = new Rectangle(refreshBtn.X - S(10) - S(130), y + S(2), S(130), S(28));
                toggleAll = new Rectangle(toggleNoCache.X - S(110), y + S(2), S(110), S(28));
                DrawButton(g, toggleAll, L.T("Alle Tokens"), small, includeCacheReads);
                DrawButton(g, toggleNoCache, L.T("Ohne Cache-Lesen"), small, !includeCacheReads);

                y += S(48);

                // Month summary line
                long monthTotal = 0; int activeDays = 0; long maxDay = 0;
                var daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
                for (int i = 0; i < daysInMonth; i++)
                {
                    var v = Value(month.AddDays(i));
                    monthTotal += v;
                    if (v > 0) activeDays++;
                    if (v > maxDay) maxDay = v;
                }
                string summary;
                if (loading) summary = L.T("Lese Claude-Code-Verläufe …");
                else if (error != null) summary = L.T("Fehler: ") + error;
                else if (data == null || data.Days.Count == 0) summary = L.T("Keine Claude-Code-Verläufe gefunden in ") + TokenHistory.ProjectsDir;
                else summary = string.Format(L.T("{0} Tokens im Monat  ·  {1} aktive Tage  ·  Ø {2} pro aktivem Tag"),
                    Tokens(monthTotal), activeDays, Tokens(activeDays == 0 ? 0 : monthTotal / activeDays));
                g.DrawString(summary, body, sub, pad, y);
                y += S(32);

                // ── Grid
                var dayNames = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
                var firstDow = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                int gap = S(6);
                int cellW = (gridW - 6 * gap) / 7;
                for (int c = 0; c < 7; c++)
                {
                    var name = dayNames[((int)firstDow + c) % 7];
                    g.DrawString(name, small, sub, pad + c * (cellW + gap) + S(4), y);
                }
                y += S(22);

                int gridBottom = ClientSize.Height - pad - S(28);
                int cellH = (gridBottom - y - 5 * gap) / 6;
                int offset = ((int)month.DayOfWeek - (int)firstDow + 7) % 7;
                var start = month.AddDays(-offset);

                for (int i = 0; i < 42; i++)
                {
                    var d = start.AddDays(i);
                    var rect = new Rectangle(pad + (i % 7) * (cellW + gap), y + (i / 7) * (cellH + gap), cellW, cellH);
                    cells[d] = rect;
                    DrawCell(g, d, rect, maxDay, body, small, d.Month == month.Month);
                }

                // Legend
                int ly = gridBottom + S(8);
                g.DrawString(L.T("Weniger"), small, sub, pad, ly);
                int lx = pad + S(58);
                for (int i = 0; i <= 4; i++)
                {
                    var r = new Rectangle(lx + i * S(20), ly + S(2), S(16), S(14));
                    using (var path = TrayIconRenderer.RoundedRect(r, S(3)))
                    using (var b = new SolidBrush(Heat(i == 0 ? 0 : i / 4.0)))
                        g.FillPath(b, path);
                }
                g.DrawString(L.T("Mehr"), small, sub, lx + 5 * S(20) + S(4), ly);

                // ── Detail panel
                DrawDetail(g, new Rectangle(ClientSize.Width - pad - detailW, pad + S(48), detailW, ClientSize.Height - 2 * pad - S(48)),
                    h2, body, small, text, sub);
            }
        }

        void DrawCell(Graphics g, DateTime d, Rectangle r, long max, Font body, Font small, bool inMonth)
        {
            var v = Value(d);
            // sqrt scale: one huge day would otherwise wash every other day out to white.
            var intensity = max > 0 && v > 0 ? Math.Sqrt((double)v / max) : 0;
            var fill = v > 0 ? Heat(0.15 + 0.85 * intensity) : pal.Surface;
            if (!inMonth) fill = Blend(fill, pal.Background, 0.6);

            using (var path = TrayIconRenderer.RoundedRect(r, S(6)))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                var border = d == selected ? pal.Text : d == DateTime.Today ? pal.Accent : pal.Border;
                using (var pen = new Pen(border, d == selected || d == DateTime.Today ? S(2) : 1)) g.DrawPath(pen, path);
            }

            var dark = intensity > 0.55;
            var fg = dark ? Color.White : (inMonth ? pal.Text : pal.SubText);
            using (var b = new SolidBrush(fg))
            {
                g.DrawString(d.Day.ToString(), small, b, r.X + S(6), r.Y + S(4));
                if (v > 0 && r.Height > S(40))
                {
                    var s = Tokens(v);
                    var sz = g.MeasureString(s, body);
                    g.DrawString(s, body, b, r.Right - sz.Width - S(4), r.Bottom - sz.Height - S(4));
                }
            }
        }

        void DrawDetail(Graphics g, Rectangle r, Font h2, Font body, Font small, Brush text, Brush sub)
        {
            using (var path = TrayIconRenderer.RoundedRect(r, S(8)))
            using (var b = new SolidBrush(pal.Surface))
            using (var pen = new Pen(pal.Border))
            {
                g.FillPath(b, path);
                g.DrawPath(pen, path);
            }

            int x = r.X + S(16), y = r.Y + S(14), w = r.Width - S(32);
            g.DrawString(selected.ToString("dddd, d. MMMM yyyy"), h2, text, new RectangleF(x, y, w, S(40)));
            y += S(36);

            var u = Day(selected);
            if (u == null)
            {
                g.DrawString(loading ? L.T("Lade …") : L.T("An diesem Tag keine Nutzung."), body, sub, x, y);
                return;
            }

            using (var big = new Font("Segoe UI Semibold", 26f * scale, GraphicsUnit.Pixel))
                g.DrawString(Tokens(u.Total(includeCacheReads)), big, text, x - S(2), y);
            y += S(40);
            g.DrawString(includeCacheReads ? L.T("Tokens gesamt") : L.T("Tokens ohne Cache-Lesen"), small, sub, x, y);
            y += S(30);

            Row(g, L.T("Input"), u.Input, x, ref y, w, body, text, sub);
            Row(g, L.T("Output"), u.Output, x, ref y, w, body, text, sub);
            Row(g, L.T("Cache schreiben"), u.CacheWrite, x, ref y, w, body, text, sub);
            Row(g, L.T("Cache lesen"), u.CacheRead, x, ref y, w, body, text, sub);
            Row(g, L.T("Antworten"), u.Messages, x, ref y, w, body, text, sub, false);

            y += S(14);
            g.DrawString(L.T("Nach Modell"), small, sub, x, y);
            y += S(22);
            var total = Math.Max(1, u.ByModel.Values.Sum());
            foreach (var kv in u.ByModel.OrderByDescending(k => k.Value))
            {
                if (y > r.Bottom - S(40)) break;
                var name = kv.Key.Replace("claude-", "");
                g.DrawString(name, small, text, x, y);
                var pct = (kv.Value * 100.0 / total).ToString("0") + " %";
                var sz = g.MeasureString(pct, small);
                g.DrawString(pct, small, sub, x + w - sz.Width, y);
                y += S(18);
                var bar = new RectangleF(x, y, w, S(5));
                using (var tb = new SolidBrush(pal.Track)) g.FillRectangle(tb, bar);
                using (var fb = new SolidBrush(pal.Accent)) g.FillRectangle(fb, x, y, (float)(w * kv.Value / (double)total), S(5));
                y += S(14);
            }
        }

        void Row(Graphics g, string label, long value, int x, ref int y, int w, Font f, Brush text, Brush sub, bool tokens = true)
        {
            g.DrawString(label, f, sub, x, y);
            var s = tokens ? Tokens(value) : value.ToString("N0");
            var sz = g.MeasureString(s, f);
            g.DrawString(s, f, text, x + w - sz.Width, y);
            y += S(24);
        }

        void DrawButton(Graphics g, Rectangle r, string label, Font f, bool active)
        {
            using (var path = TrayIconRenderer.RoundedRect(r, S(6)))
            {
                using (var b = new SolidBrush(active ? pal.Accent : pal.Surface)) g.FillPath(b, path);
                using (var pen = new Pen(active ? pal.Accent : pal.Border)) g.DrawPath(pen, path);
            }
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(active ? Color.White : pal.Text))
                g.DrawString(label, f, b, r, sf);
        }

        Color Heat(double t)
        {
            // From a pale tint of Claude orange up to a deep burnt orange.
            var light = Blend(pal.Accent, pal.Surface, 0.8);
            var deep = Color.FromArgb(170, 70, 35);
            return t <= 0 ? pal.Surface : Blend(deep, light, t);
        }

        static Color Blend(Color a, Color b, double t)
        {
            // t = weight of a
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R * t + b.R * (1 - t)), (int)(a.G * t + b.G * (1 - t)), (int)(a.B * t + b.B * (1 - t)));
        }

        public static string Tokens(long v)
        {
            if (v >= 1000000000) return (v / 1e9).ToString("0.##") + L.T(" Mrd.");
            if (v >= 1000000) return (v / 1e6).ToString(v >= 100000000 ? "0" : "0.#") + " M";
            if (v >= 1000) return (v / 1e3).ToString("0") + " K";
            return v.ToString();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var p = e.Location;
            if (prevBtn.Contains(p)) ShiftMonth(-1);
            else if (nextBtn.Contains(p)) ShiftMonth(1);
            else if (todayBtn.Contains(p)) { month = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); selected = DateTime.Today; }
            else if (refreshBtn.Contains(p)) Reload();
            else if (toggleAll.Contains(p)) includeCacheReads = true;
            else if (toggleNoCache.Contains(p)) includeCacheReads = false;
            else
            {
                foreach (var kv in cells)
                {
                    if (!kv.Value.Contains(p)) continue;
                    selected = kv.Key;
                    if (kv.Key.Month != month.Month || kv.Key.Year != month.Year)
                        month = new DateTime(kv.Key.Year, kv.Key.Month, 1);
                    break;
                }
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var p = e.Location;
            var hand = prevBtn.Contains(p) || nextBtn.Contains(p) || todayBtn.Contains(p) || refreshBtn.Contains(p)
                || toggleAll.Contains(p) || toggleNoCache.Contains(p) || cells.Values.Any(r => r.Contains(p));
            Cursor = hand ? Cursors.Hand : Cursors.Default;
        }

        void ShiftMonth(int delta)
        {
            month = month.AddMonths(delta);
            selected = new DateTime(month.Year, month.Month, Math.Min(selected.Day, DateTime.DaysInMonth(month.Year, month.Month)));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left: Select(selected.AddDays(-1)); return true;
                case Keys.Right: Select(selected.AddDays(1)); return true;
                case Keys.Up: Select(selected.AddDays(-7)); return true;
                case Keys.Down: Select(selected.AddDays(7)); return true;
                case Keys.PageUp: ShiftMonth(-1); Invalidate(); return true;
                case Keys.PageDown: ShiftMonth(1); Invalidate(); return true;
                case Keys.F5: Reload(); return true;
                case Keys.Escape: Close(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void Select(DateTime d)
        {
            selected = d;
            month = new DateTime(d.Year, d.Month, 1);
            Invalidate();
        }
    }
}
