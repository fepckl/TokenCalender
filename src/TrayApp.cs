using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ClaudeUsageTracker
{
    class TrayApp : ApplicationContext
    {
        readonly AppSettings settings;
        readonly NotifyIcon tray;
        readonly UsagePopup popup;
        readonly Notifier notifier;
        readonly Timer timer;
        readonly ToolStripMenuItem autostartItem;
        CalendarForm calendar;
        Icon currentIcon;
        UsageSnapshot snapshot;
        string error;
        bool refreshing;
        int backoffFactor = 1;

        public TrayApp()
        {
            settings = AppSettings.Load();

            popup = new UsagePopup();
            popup.RefreshClicked += delegate { RefreshNow(); };
            popup.SettingsClicked += delegate { OpenSettings(); };
            popup.CalendarClicked += delegate { OpenCalendar(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(L.T("Aktualisieren"), null, delegate { RefreshNow(); });
            menu.Items.Add(L.T("Token-Kalender …"), null, delegate { OpenCalendar(); });
            menu.Items.Add(L.T("Nutzung auf claude.ai öffnen"), null, delegate { OpenUrl("https://claude.ai/settings/usage"); });
            menu.Items.Add(new ToolStripSeparator());
            autostartItem = new ToolStripMenuItem(L.T("Mit Windows starten"), null, delegate { ToggleAutostart(); });
            menu.Items.Add(autostartItem);
            menu.Items.Add(L.T("Einstellungen …"), null, delegate { OpenSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(L.T("Beenden"), null, delegate { ExitThread(); });
            menu.Opening += delegate { autostartItem.Checked = Autostart.IsEnabled; };

            tray = new NotifyIcon { ContextMenuStrip = menu, Text = "Claude Usage", Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) popup.Toggle(); };
            notifier = new Notifier(tray, settings);

            timer = new Timer();
            timer.Tick += delegate { RefreshNow(); };

            UpdateTray();
            RefreshNow();
        }

        async void RefreshNow()
        {
            if (refreshing) return;
            refreshing = true;
            timer.Stop();
            PushState();
            try
            {
                var service = new UsageService(settings);
                var snap = await Task.Run(() => service.Fetch());
                snapshot = snap;
                error = null;
                backoffFactor = 1;
                notifier.Process(snap);
            }
            catch (UsageException ex)
            {
                error = ex.Message;
                // Back off on transient errors (429, network) so we don't hammer the API.
                if (!ex.NeedsUserAction) backoffFactor = Math.Min(backoffFactor * 2, 8);
            }
            catch (Exception ex)
            {
                error = L.T("Fehler: ") + ex.Message;
                backoffFactor = Math.Min(backoffFactor * 2, 8);
            }
            finally
            {
                refreshing = false;
                PushState();
                timer.Interval = Math.Max(30, settings.RefreshSeconds) * 1000 * backoffFactor;
                timer.Start();
            }
        }

        void PushState()
        {
            popup.SetState(snapshot, error, refreshing, settings.ShowRemaining);
            UpdateTray();
        }

        UsageWindow TrayWindow()
        {
            if (snapshot == null) return null;
            if (settings.TrayMetric == "weekly") return snapshot.Weekly;
            if (settings.TrayMetric == "max")
            {
                var max = snapshot.Session;
                if (snapshot.Weekly.Percent > max.Percent) max = snapshot.Weekly;
                foreach (var m in snapshot.Models) if (m.Percent > max.Percent) max = m;
                return max;
            }
            return snapshot.Session;
        }

        void UpdateTray()
        {
            int size = SystemInformation.SmallIconSize.Width;
            string text;
            Color color;
            var w = TrayWindow();
            if (w == null)
            {
                text = error != null ? "!" : "…";
                color = error != null ? Palette.Critical : Color.Gray;
            }
            else
            {
                text = Math.Round(w.DisplayPercent(settings.ShowRemaining)).ToString("0");
                color = error != null ? Color.Gray : Palette.ForStatus(w.Status());
            }

            var old = currentIcon;
            currentIcon = TrayIconRenderer.Render(text, color, size);
            tray.Icon = currentIcon;
            if (old != null) old.Dispose();

            // NotifyIcon.Text is limited to 63 characters.
            string tip;
            if (snapshot == null) tip = error ?? "Claude Usage";
            else
            {
                tip = string.Format("Session {0}  ·  {1} {2}",
                    Format.Percent(snapshot.Session.DisplayPercent(settings.ShowRemaining)),
                    L.T("Woche"),
                    Format.Percent(snapshot.Weekly.DisplayPercent(settings.ShowRemaining)));
                var cd = Format.Countdown(snapshot.Session.ResetsAtUtc);
                if (cd.Length > 0) tip += "\n" + cd;
            }
            tray.Text = tip.Length > 63 ? tip.Substring(0, 62) + "…" : tip;
        }

        void OpenSettings()
        {
            using (var f = new SettingsForm(settings))
            {
                if (f.ShowDialog() == DialogResult.OK)
                {
                    backoffFactor = 1;
                    RefreshNow();
                }
            }
        }

        void OpenCalendar()
        {
            if (calendar == null || calendar.IsDisposed)
            {
                calendar = new CalendarForm();
                calendar.Show();
            }
            else
            {
                if (calendar.WindowState == FormWindowState.Minimized) calendar.WindowState = FormWindowState.Normal;
                calendar.Activate();
            }
        }

        void ToggleAutostart()
        {
            try { Autostart.Set(!Autostart.IsEnabled); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Claude Usage", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            popup.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            base.ExitThreadCore();
        }
    }
}
