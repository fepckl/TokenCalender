using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClaudeUsageTracker
{
    /// Claude Code on Windows keeps its OAuth tokens in plain JSON at
    /// %USERPROFILE%\.claude\.credentials.json (macOS uses the Keychain instead).
    static class ClaudeCodeCredentials
    {
        public static string FilePath
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
                return Path.Combine(dir, ".credentials.json");
            }
        }

        public static Dictionary<string, object> Load()
        {
            var path = FilePath;
            if (!File.Exists(path))
                throw new UsageException(L.T("Keine Claude-Code-Anmeldung gefunden. Installiere Claude Code und führe `claude` → /login aus, oder hinterlege in den Einstellungen einen claude.ai-Session-Key."), true);
            var json = Json.ParseObject(File.ReadAllText(path, Encoding.UTF8));
            if (Json.Obj(json, "claudeAiOauth") == null)
                throw new UsageException(L.T("Die Claude-Code-Anmeldedaten enthalten kein OAuth-Token (API-Key-Login?)."), true);
            return json;
        }

        public static bool IsExpired(Dictionary<string, object> root)
        {
            var expiresAt = Json.Num(Json.Obj(root, "claudeAiOauth"), "expiresAt");
            if (!expiresAt.HasValue) return false;
            var expiry = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(expiresAt.Value);
            return expiry <= DateTime.UtcNow;
        }

        /// Merges a refreshed token back into the file so Claude Code keeps working:
        /// the refresh token is single-use, so not writing it back would force a /login.
        public static void SaveRefreshed(Dictionary<string, object> root, TokenResponse t)
        {
            var oauth = Json.Obj(root, "claudeAiOauth");
            oauth["accessToken"] = t.AccessToken;
            if (!string.IsNullOrEmpty(t.RefreshToken)) oauth["refreshToken"] = t.RefreshToken;
            var nowMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            oauth["expiresAt"] = nowMs + (long)(t.ExpiresInSeconds * 1000);

            // Write-then-replace so a crash never leaves a half-written file behind.
            var path = FilePath;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Serialize(root), new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
    }

    class UsageService
    {
        readonly AppSettings settings;

        public UsageService(AppSettings settings)
        {
            this.settings = settings;
        }

        /// Blocking — call from a background thread.
        public UsageSnapshot Fetch()
        {
            if (settings.Source == AppSettings.SourceSessionKey)
            {
                var key = settings.GetSessionKey();
                if (string.IsNullOrEmpty(key))
                    throw new UsageException(L.T("Kein Session-Key hinterlegt (Einstellungen)."), true);
                return ClaudeApi.FetchWithSessionKey(key);
            }
            return FetchViaClaudeCode();
        }

        UsageSnapshot FetchViaClaudeCode()
        {
            var root = ClaudeCodeCredentials.Load();

            // Claude Code refreshes its token shortly before expiry while it is running.
            // An already-expired token means the CLI is idle, so it is safe for us to
            // take over the rotation once (same rule as the macOS app).
            if (ClaudeCodeCredentials.IsExpired(root)) root = Refresh(root);

            var result = ClaudeApi.GetOAuthUsage(Token(root));
            if (result.Status == 401)
            {
                // Re-read first: Claude Code may have rotated the token in the meantime.
                root = ClaudeCodeCredentials.Load();
                if (ClaudeCodeCredentials.IsExpired(root)) root = Refresh(root);
                result = ClaudeApi.GetOAuthUsage(Token(root));
            }
            if (result.Status == 401) throw new UsageException(L.T("Claude-Code-Anmeldung abgelaufen. Bitte `claude` starten und /login ausführen."), true);
            if (result.Status == 429) throw new UsageException(L.T("Zu viele Anfragen – das Intervall wird automatisch verlängert."));
            if (result.Status != 200) throw new UsageException("api.anthropic.com: HTTP " + result.Status);

            var snap = UsageParser.Parse(result.Body);
            snap.Source = "Claude Code";
            snap.Plan = Json.Str(Json.Obj(root, "claudeAiOauth"), "subscriptionType");
            return snap;
        }

        static string Token(Dictionary<string, object> root)
        {
            return Json.Str(Json.Obj(root, "claudeAiOauth"), "accessToken");
        }

        static Dictionary<string, object> Refresh(Dictionary<string, object> root)
        {
            var refresh = Json.Str(Json.Obj(root, "claudeAiOauth"), "refreshToken");
            if (string.IsNullOrEmpty(refresh))
                throw new UsageException(L.T("Claude-Code-Anmeldung abgelaufen. Bitte `claude` starten und /login ausführen."), true);
            var t = ClaudeApi.RefreshToken(refresh);
            ClaudeCodeCredentials.SaveRefreshed(root, t);
            return root;
        }
    }
}
