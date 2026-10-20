using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ClaudeUsageTracker
{
    /// Borderless flyout shown above the tray, the counterpart of the macOS popover.
    class UsagePopup : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWCP_ROUND = 2;

        public event EventHandler RefreshClicked;
        public event EventHandler SettingsClicked;
        public event EventHandler CalendarClicked;

        UsageSnapshot snapshot;
        string error;
        bool refreshing;
        bool showRemaining;
        Palette pal;
        float scale = 1f;
        Rectangle refreshLink, settingsLink, calendarLink;
        DateTime lastHidden = DateTime.MinValue;
        readonly Timer tick = new Timer { Interval = 30000 };

        public UsagePopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "Claude Usage";
            tick.Tick += delegate { if (Visible) Invalidate(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;     // WS_EX_TOOLWINDOW: keep it out of Alt+Tab
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = DWMWCP_ROUND;
            try { DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int)); } catch { /* pre-Win11 */ }
        }

        public void SetState(UsageSnapshot snap, string err, bool isRefreshing, bool remaining)
        {
            snapshot = snap;
            error = err;
            refreshing = isRefreshing;
            showRemaining = remaining;
            if (Visible) { Relayout(); Invalidate(); }
        }

        /// Toggle; ignores the click that just closed the popup via Deactivate.
        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            if ((DateTime.Now - lastHidden).TotalMilliseconds < 250) return;
            ShowNearTray();
        }

        void ShowNearTray()
        {
            pal = Palette.Current();
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Relayout();

            var cursor = Cursor.Position;
            var screen = Screen.FromPoint(cursor);
            var wa = screen.WorkingArea;
            var b = screen.Bounds;
            int margin = S(12);
            int x, y;
            if (wa.Bottom < b.Bottom) { x = cursor.X - Width / 2; y = wa.Bottom - Height - margin; }       // taskbar bottom
            else if (wa.Top > b.Top) { x = cursor.X - Width / 2; y = wa.Top + margin; }                   // top
            else if (wa.Left > b.Left) { x = wa.Left + margin; y = cursor.Y - Height / 2; }               // left
            else { x = wa.Right - Width - margin; y = Math.Min(cursor.Y, wa.Bottom) - Height / 2; }       // right / auto-hide
            x = Math.Max(wa.Left + margin, Math.Min(x, wa.Right - Width - margin));
            y = Math.Max(wa.Top + margin, Math.Min(y, wa.Bottom - Height - margin));
            Location = new Point(x, y);

            BackColor = pal.Background;
            Show();
            Activate();
            tick.Start();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Hide();
            lastHidden = DateTime.Now;
            tick.Stop();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Hide();
            else if (e.KeyCode == Keys.F5 && RefreshClicked != null) RefreshClicked(this, EventArgs.Empty);
            base.OnKeyDown(e);
        }

        int S(float v) { return (int)Math.Round(v * scale); }

        IEnumerable<UsageWindow> Rows()
        {
            if (snapshot == null) yield break;
            yield return snapshot.Session;
            yield return snapshot.Weekly;
            foreach (var m in snapshot.Models.OrderByDescending(m => m.Percent).ThenBy(m => m.Name))
                yield return m;
        }

        void Relayout()
        {
            int h = S(64);                                  // header
            if (error != null) h += S(snapshot == null ? 84 : 58);
            h += Rows().Count() * S(62);
            if (snapshot != null && snapshot.ExtraLimit.HasValue) h += S(30);
            if (snapshot == null && error == null) h += S(60);
            h += S(48);                                     // footer
            Size = new Size(S(340), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (pal == null) pal = Palette.Current();
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(pal.Background);
            using (var pen = new Pen(pal.Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

            int pad = S(18);
            int w = Width - 2 * pad;
            int y = S(16);

            using (var title = new Font("Segoe UI Semibold", 13f * scale, GraphicsUnit.Pixel))
            using (var titleBig = new Font("Segoe UI Semibold", 15f * scale, GraphicsUnit.Pixel))
            using (var body = new Font("Segoe UI", 12f * scale, GraphicsUnit.Pixel))
            using (var small = new Font("Segoe UI", 11f * scale, GraphicsUnit.Pixel))
            using (var text = new SolidBrush(pal.Text))
            using (var sub = new SolidBrush(pal.SubText))
            using (var accent = new SolidBrush(pal.Accent))
            {
                // Header: brand dot, title, plan badge
                g.FillEllipse(accent, pad, y + S(5), S(12), S(12));
                g.DrawString("Claude Usage", titleBig, text, pad + S(18), y);
                if (snapshot != null && !string.IsNullOrEmpty(snapshot.Plan))
                {
                    var plan = char.ToUpper(snapshot.Plan[0]) + snapshot.Plan.Substring(1);
                    var sz = g.MeasureString(plan, small);
                    var badge = new RectangleF(pad + w - sz.Width - S(12), y + S(2), sz.Width + S(12), S(20));
                    using (var path = TrayIconRenderer.RoundedRect(badge, S(10) / 2f))
                    using (var bb = new SolidBrush(Color.FromArgb(40, pal.Accent)))
                        g.FillPath(bb, path);
                    g.DrawString(plan, small, accent, badge.X + S(6), badge.Y + S(2));
                }
                y += S(26);
                var subtitle = snapshot != null ? L.T("Quelle: ") + snapshot.Source : L.T("Noch keine Daten");
                if (refreshing) subtitle = L.T("Aktualisiere …");
                g.DrawString(subtitle, small, sub, pad + S(18), y);
                y += S(22);

                if (error != null)
                {
                    int eh = S(snapshot == null ? 72 : 46);
                    var box = new RectangleF(pad, y, w, eh);
                    using (var path = TrayIconRenderer.RoundedRect(box, S(6)))
                    using (var eb = new SolidBrush(Color.FromArgb(36, Palette.Critical)))
                        g.FillPath(eb, path);
                    g.DrawString(error, small, text, new RectangleF(pad + S(8), y + S(6), w - S(16), eh - S(10)));
                    y += eh + S(12);
                }
                else if (snapshot == null)
                {
                    g.DrawString(L.T("Lade Nutzungsdaten …"), body, sub, pad, y + S(18));
                    y += S(60);
                }

                foreach (var row in Rows())
                {
                    DrawRow(g, row, pad, y, w, title, small, text, sub);
                    y += S(62);
                }

                if (snapshot != null && snapshot.ExtraLimit.HasValue)
                {
                    var cur = snapshot.ExtraCurrency ?? "";
                    var s = string.Format("{0}: {1:0.00} / {2:0.00} {3}", L.T("Extra-Nutzung"), snapshot.ExtraUsed ?? 0, snapshot.ExtraLimit.Value, cur);
                    g.DrawString(s, small, sub, pad, y);
                    y += S(30);
                }

                // Footer
                using (var pen = new Pen(pal.Border)) g.DrawLine(pen, 0, y, Width, y);
                y += S(14);
                var updated = snapshot == null ? "" : snapshot.FetchedAtUtc.ToLocalTime().ToString("t");
                g.DrawString(updated, small, sub, pad, y);

                var settingsText = L.T("Einstellungen");
                var refreshText = L.T("Aktualisieren");
                var sSize = g.MeasureString(settingsText, small);
                var rSize = g.MeasureString(refreshText, small);
                settingsLink = new Rectangle((int)(pad + w - sSize.Width), y, (int)sSize.Width, (int)sSize.Height);
                refreshLink = new Rectangle((int)(settingsLink.X - S(14) - rSize.Width), y, (int)rSize.Width, (int)rSize.Height);
                var calendarText = L.T("Kalender");
                var cSize = g.MeasureString(calendarText, small);
                calendarLink = new Rectangle((int)(refreshLink.X - S(14) - cSize.Width), y, (int)cSize.Width, (int)cSize.Height);
                g.DrawString(calendarText, small, accent, calendarLink.X, y);
                g.DrawString(refreshText, small, accent, refreshLink.X, y);
                g.DrawString(settingsText, small, accent, settingsLink.X, y);
            }
        }

        void DrawRow(Graphics g, UsageWindow row, int x, int y, int w, Font title, Font small, Brush text, Brush sub)
        {
            var status = row.Status();
            var color = Palette.ForStatus(status);
            var shown = row.DisplayPercent(showRemaining);

            g.DrawString(row.Name, title, text, x, y);
            var pct = Format.Percent(shown) + (showRemaining ? L.T(" frei") : "");
            var psz = g.MeasureString(pct, title);
            using (var cb = new SolidBrush(color)) g.DrawString(pct, title, cb, x + w - psz.Width, y);

            int barY = y + S(22);
            int barH = S(6);
            var track = new RectangleF(x, barY, w, barH);
            using (var path = TrayIconRenderer.RoundedRect(track, barH / 2f))
            using (var tb = new SolidBrush(pal.Track))
                g.FillPath(tb, path);
            var fillW = (float)(w * Math.Min(100, Math.Max(0, shown)) / 100.0);
            if (fillW >= barH)
            {
                using (var path = TrayIconRenderer.RoundedRect(new RectangleF(x, barY, fillW, barH), barH / 2f))
                using (var fb = new SolidBrush(color))
                    g.FillPath(fb, path);
            }

            // Pace marker: where usage "should" be if spread evenly over the window.
            var elapsed = row.ElapsedFraction();
            if (elapsed.HasValue && elapsed.Value > 0 && elapsed.Value < 1)
            {
                var frac = showRemaining ? 1 - elapsed.Value : elapsed.Value;
                var mx = x + (float)(w * frac);
                using (var pen = new Pen(pal.Text, Math.Max(1f, scale * 1.5f)))
                    g.DrawLine(pen, mx, barY - S(3), mx, barY + barH + S(3));
            }

            var info = Format.Countdown(row.ResetsAtUtc);
            var clock = Format.ResetClock(row.ResetsAtUtc);
            if (clock.Length > 0) info += "  ·  " + clock;
            g.DrawString(info, small, sub, x, barY + barH + S(5));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = refreshLink.Contains(e.Location) || settingsLink.Contains(e.Location) || calendarLink.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (refreshLink.Contains(e.Location) && RefreshClicked != null) RefreshClicked(this, EventArgs.Empty);
            else if (settingsLink.Contains(e.Location) && SettingsClicked != null) { Hide(); SettingsClicked(this, EventArgs.Empty); }
            else if (calendarLink.Contains(e.Location) && CalendarClicked != null) { Hide(); CalendarClicked(this, EventArgs.Empty); }
        }
    }
}
