using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace ClaudeUsageTracker
{
    class AppSettings
    {
        public const string SourceClaudeCode = "claudecode";
        public const string SourceSessionKey = "sessionkey";

        public string Source { get; set; }
        /// DPAPI-encrypted (CurrentUser scope), base64 — the Windows stand-in for the macOS Keychain.
        public string SessionKeyProtected { get; set; }
        public int RefreshSeconds { get; set; }
        public bool ShowRemaining { get; set; }
        public string TrayMetric { get; set; }   // "session" | "weekly" | "max"
        public bool NotificationsEnabled { get; set; }
        public int[] Thresholds { get; set; }
        public bool NotifyOnReset { get; set; }

        public AppSettings()
        {
            Source = SourceClaudeCode;
            RefreshSeconds = 120;
            TrayMetric = "session";
            NotificationsEnabled = true;
            Thresholds = new[] { 75, 90, 95 };
            NotifyOnReset = true;
        }

        static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageTracker"); }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = Json.Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (s != null)
                    {
                        if (s.RefreshSeconds < 30) s.RefreshSeconds = 30;
                        if (s.Thresholds == null) s.Thresholds = new int[0];
                        return s;
                    }
                }
            }
            catch { /* corrupt settings fall back to defaults */ }
            return new AppSettings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, Json.Serialize(this), new UTF8Encoding(false));
        }

        public string GetSessionKey()
        {
            if (string.IsNullOrEmpty(SessionKeyProtected)) return null;
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(SessionKeyProtected), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch { return null; }
        }

        public void SetSessionKey(string key)
        {
            if (string.IsNullOrEmpty(key)) { SessionKeyProtected = null; return; }
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
            SessionKeyProtected = Convert.ToBase64String(bytes);
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "ClaudeUsageTracker";

        public static bool IsEnabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"");
                else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName);
            }
        }
    }

    static class WindowsTheme
    {
        /// Taskbar/flyouts follow SystemUsesLightTheme, not AppsUseLightTheme.
        public static bool IsDark
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        var v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                        return v is int && (int)v == 0;
                    }
                }
                catch { return false; }
            }
        }
    }
}
