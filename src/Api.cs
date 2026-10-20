using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace ClaudeUsageTracker
{
    class HttpResult
    {
        public int Status;
        public string Body;
        public WebHeaderCollection Headers;
    }

    static class Http
    {
        static Http()
        {
            // .NET Framework defaults to TLS 1.0 for older target versions.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

        public static HttpResult Send(string method, string url, IDictionary<string, string> headers, string body, string contentType)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (headers != null)
            {
                foreach (var h in headers)
                {
                    if (h.Key == "User-Agent") req.UserAgent = h.Value;
                    else if (h.Key == "Accept") req.Accept = h.Value;
                    else if (h.Key == "Referer") req.Referer = h.Value;
                    else req.Headers[h.Key] = h.Value;
                }
            }
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                req.ContentType = contentType;
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }

            HttpWebResponse resp;
            try
            {
                resp = (HttpWebResponse)req.GetResponse();
            }
            catch (WebException ex)
            {
                resp = ex.Response as HttpWebResponse;
                if (resp == null) throw new UsageException(L.T("Netzwerkfehler: ") + ex.Message);
            }
            using (resp)
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                return new HttpResult { Status = (int)resp.StatusCode, Body = reader.ReadToEnd(), Headers = resp.Headers };
            }
        }
    }

    /// Talks to the two usage endpoints the macOS app uses.
    static class ClaudeApi
    {
        const string OAuthUsageUrl = "https://api.anthropic.com/api/oauth/usage";
        const string OAuthTokenUrl = "https://platform.claude.com/v1/oauth/token";
        const string ClaudeAiBase = "https://claude.ai/api";
        // Public client id of Claude Code, as used by the original app.
        const string ClaudeCodeClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        const string BrowserUA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

        public static HttpResult GetOAuthUsage(string accessToken)
        {
            var headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer " + accessToken },
                { "anthropic-beta", "oauth-2025-04-20" },
                { "User-Agent", "claude-code/2.1.5" },
                { "Accept", "application/json" }
            };
            return Http.Send("GET", OAuthUsageUrl, headers, null, null);
        }

        public static TokenResponse RefreshToken(string refreshToken)
        {
            var clientId = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_CLIENT_ID") ?? ClaudeCodeClientId;
            var body = "grant_type=refresh_token"
                + "&refresh_token=" + Uri.EscapeDataString(refreshToken)
                + "&client_id=" + Uri.EscapeDataString(clientId);
            var r = Http.Send("POST", OAuthTokenUrl, null, body, "application/x-www-form-urlencoded");
            if (r.Status != 200)
                throw new UsageException(L.T("Token-Erneuerung fehlgeschlagen (HTTP ") + r.Status + L.T("). Bitte `claude` starten und ggf. /login ausführen."), true);
            var json = Json.ParseObject(r.Body);
            var access = Json.Str(json, "access_token");
            var expires = Json.Num(json, "expires_in");
            if (string.IsNullOrEmpty(access) || !expires.HasValue)
                throw new UsageException(L.T("Token-Erneuerung lieferte eine unerwartete Antwort."));
            return new TokenResponse
            {
                AccessToken = access,
                RefreshToken = Json.Str(json, "refresh_token"),
                ExpiresInSeconds = expires.Value
            };
        }

        public static UsageSnapshot FetchWithSessionKey(string sessionKey)
        {
            var orgs = ClaudeAiGet("/organizations", sessionKey);
            var list = Json.Deserialize<List<Dictionary<string, object>>>(orgs);
            if (list == null || list.Count == 0) throw new UsageException(L.T("Keine Organisation für diesen Session-Key gefunden."), true);

            // Prefer the org that has chat (the consumer plan); API-only orgs have no usage limits.
            string orgId = null;
            foreach (var org in list)
            {
                var caps = Json.Arr(org, "capabilities");
                if (caps != null && caps.Contains("chat")) { orgId = Json.Str(org, "uuid"); break; }
            }
            if (orgId == null) orgId = Json.Str(list[0], "uuid");

            var snap = UsageParser.Parse(ClaudeAiGet("/organizations/" + orgId + "/usage", sessionKey));
            snap.Source = "claude.ai";
            return snap;
        }

        static string ClaudeAiGet(string path, string sessionKey)
        {
            var headers = new Dictionary<string, string>
            {
                { "Cookie", "sessionKey=" + sessionKey },
                { "Accept", "application/json" },
                { "User-Agent", BrowserUA },
                { "Referer", "https://claude.ai" },
                { "Origin", "https://claude.ai" }
            };
            var r = Http.Send("GET", ClaudeAiBase + path, headers, null, null);
            if (r.Status == 200) return r.Body;
            if (r.Body != null && r.Body.Contains("Just a moment"))
                throw new UsageException(L.T("claude.ai blockiert die Anfrage (Cloudflare). Nutze besser die Claude-Code-Anmeldung."));
            if (r.Status == 401 || r.Status == 403)
                throw new UsageException(L.T("Session-Key ungültig oder abgelaufen."), true);
            throw new UsageException("claude.ai: HTTP " + r.Status);
        }
    }

    class TokenResponse
    {
        public string AccessToken;
        public string RefreshToken;
        public double ExpiresInSeconds;
    }
}
