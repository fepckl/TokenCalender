using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace ClaudeUsageTracker
{
    /// Compares consecutive snapshots and raises tray balloon notifications.
    class Notifier
    {
        readonly NotifyIcon tray;
        readonly AppSettings settings;
        UsageSnapshot previous;

        public Notifier(NotifyIcon tray, AppSettings settings)
        {
            this.tray = tray;
            this.settings = settings;
        }

        public void Process(UsageSnapshot current)
        {
            var prev = previous;
            previous = current;
            // First snapshot after start: nothing to compare against, stay quiet.
            if (prev == null || !settings.NotificationsEnabled) return;

            Check(prev.Session, current.Session);
            Check(prev.Weekly, current.Weekly);

            if (settings.NotifyOnReset && prev.Session.Percent >= 1 && current.Session.Percent < prev.Session.Percent - 25)
                Show(L.T("Session zurückgesetzt"), L.T("Dein 5-Stunden-Kontingent ist wieder frei."), ToolTipIcon.Info);
        }

        void Check(UsageWindow before, UsageWindow now)
        {
            var crossed = CrossedThresholds(before.Percent, now.Percent, settings.Thresholds);
            if (crossed.Count == 0) return;
            // Several thresholds at once (e.g. after sleep): only announce the highest.
            var top = crossed[crossed.Count - 1];
            Show(string.Format(L.T("{0}: {1} % erreicht"), now.Name, top),
                 Format.Countdown(now.ResetsAtUtc),
                 top >= 90 ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }

        /// Returns the thresholds (ascending) that should trigger a notification for
        /// the move from `previousPercent` to `currentPercent`. Both values are 0-100.
        internal static List<int> CrossedThresholds(double previousPercent, double currentPercent, int[] thresholds)
        {
            var result = new List<int>();
            if (thresholds == null) return result;
            // Upward crossings only: a reset (95 -> 2) or a small dip never fires.
            foreach (var t in thresholds)
                if (previousPercent < t && currentPercent >= t && !result.Contains(t)) result.Add(t);
            result.Sort();
            return result;
        }

        void Show(string title, string text, ToolTipIcon icon)
        {
            tray.ShowBalloonTip(5000, title, string.IsNullOrEmpty(text) ? " " : text, icon);
        }
    }
}
