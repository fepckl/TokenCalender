using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ClaudeUsageTracker
{
    class SettingsForm : Form
    {
        readonly AppSettings settings;
        readonly RadioButton rbClaudeCode, rbSessionKey;
        readonly TextBox tbSessionKey;
        readonly NumericUpDown nudInterval;
        readonly ComboBox cbTray;
        readonly CheckBox chkRemaining, chkNotify, chkReset, chkAutostart;
        readonly TextBox tbThresholds;

        public SettingsForm(AppSettings settings)
        {
            this.settings = settings;
            Text = L.T("Claude Usage – Einstellungen");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = new Font("Segoe UI", 9f);
            Padding = new Padding(12);

            var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));

            rbClaudeCode = new RadioButton { Text = L.T("Claude-Code-Anmeldung (empfohlen)"), AutoSize = true };
            rbSessionKey = new RadioButton { Text = L.T("claude.ai-Session-Key"), AutoSize = true };
            tbSessionKey = new TextBox { Width = 290, UseSystemPasswordChar = true };
            var keyHint = new Label
            {
                AutoSize = true, MaximumSize = new Size(300, 0), ForeColor = SystemColors.GrayText,
                Text = L.T("Browser → claude.ai → F12 → Anwendung → Cookies → Wert von „sessionKey“ (beginnt mit sk-ant-sid…). Wird mit Windows-DPAPI verschlüsselt gespeichert.")
            };
            rbSessionKey.CheckedChanged += delegate { tbSessionKey.Enabled = rbSessionKey.Checked; };

            nudInterval = new NumericUpDown { Minimum = 30, Maximum = 3600, Increment = 30, Width = 80 };
            cbTray = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
            cbTray.Items.AddRange(new object[] { L.T("Session (5 h)"), L.T("Woche"), L.T("Höchster Wert") });
            chkRemaining = new CheckBox { Text = L.T("Verbleibend statt verbraucht anzeigen"), AutoSize = true };
            chkNotify = new CheckBox { Text = L.T("Benachrichtigungen bei Schwellenwerten"), AutoSize = true };
            tbThresholds = new TextBox { Width = 140 };
            chkReset = new CheckBox { Text = L.T("Benachrichtigen, wenn die Session zurückgesetzt wird"), AutoSize = true };
            chkAutostart = new CheckBox { Text = L.T("Mit Windows starten"), AutoSize = true };
            chkNotify.CheckedChanged += delegate { tbThresholds.Enabled = chkNotify.Checked; };

            AddRow(t, L.T("Datenquelle"), rbClaudeCode);
            AddRow(t, "", rbSessionKey);
            AddRow(t, "", tbSessionKey);
            AddRow(t, "", keyHint);
            AddRow(t, L.T("Intervall (Sek.)"), nudInterval);
            AddRow(t, L.T("Im Tray anzeigen"), cbTray);
            AddRow(t, "", chkRemaining);
            AddRow(t, L.T("Hinweise"), chkNotify);
            AddRow(t, L.T("Schwellen (%)"), tbThresholds);
            AddRow(t, "", chkReset);
            AddRow(t, L.T("System"), chkAutostart);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = L.T("Abbrechen"), DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            t.Controls.Add(buttons, 0, t.RowCount);
            t.SetColumnSpan(buttons, 2);
            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(t);

            // Populate
            rbClaudeCode.Checked = settings.Source != AppSettings.SourceSessionKey;
            rbSessionKey.Checked = !rbClaudeCode.Checked;
            tbSessionKey.Text = settings.GetSessionKey() ?? "";
            tbSessionKey.Enabled = rbSessionKey.Checked;
            nudInterval.Value = Math.Max(30, Math.Min(3600, settings.RefreshSeconds));
            cbTray.SelectedIndex = settings.TrayMetric == "weekly" ? 1 : settings.TrayMetric == "max" ? 2 : 0;
            chkRemaining.Checked = settings.ShowRemaining;
            chkNotify.Checked = settings.NotificationsEnabled;
            tbThresholds.Text = string.Join(", ", settings.Thresholds);
            tbThresholds.Enabled = chkNotify.Checked;
            chkReset.Checked = settings.NotifyOnReset;
            chkAutostart.Checked = Autostart.IsEnabled;

            ok.Click += OnOk;
        }

        static void AddRow(TableLayoutPanel t, string label, Control c)
        {
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label.Length > 0)
                t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 0) }, 0, row);
            c.Margin = new Padding(0, 3, 0, 3);
            t.Controls.Add(c, 1, row);
        }

        void OnOk(object sender, EventArgs e)
        {
            int dummy;
            var thresholds = tbThresholds.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => int.TryParse(s.Trim(), out dummy))
                .Select(s => int.Parse(s.Trim()))
                .Where(v => v > 0 && v <= 100)
                .Distinct().OrderBy(v => v).ToArray();

            if (rbSessionKey.Checked && tbSessionKey.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, L.T("Bitte einen Session-Key eingeben."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            settings.Source = rbSessionKey.Checked ? AppSettings.SourceSessionKey : AppSettings.SourceClaudeCode;
            settings.SetSessionKey(tbSessionKey.Text);
            settings.RefreshSeconds = (int)nudInterval.Value;
            settings.TrayMetric = cbTray.SelectedIndex == 1 ? "weekly" : cbTray.SelectedIndex == 2 ? "max" : "session";
            settings.ShowRemaining = chkRemaining.Checked;
            settings.NotificationsEnabled = chkNotify.Checked;
            settings.Thresholds = thresholds;
            settings.NotifyOnReset = chkReset.Checked;
            settings.Save();
            try { Autostart.Set(chkAutostart.Checked); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
}
