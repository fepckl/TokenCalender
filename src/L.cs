using System.Collections.Generic;
using System.Globalization;

namespace ClaudeUsageTracker
{
    /// Minimal localization: source strings are German, English is used for every
    /// other UI language.
    static class L
    {
        static readonly bool German = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

        static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "Session (5 h)", "Session (5 h)" },
            { "Woche (alle Modelle)", "Weekly (all models)" },
            { "Woche", "Week" },
            { "Höchster Wert", "Highest value" },
            { "Netzwerkfehler: ", "Network error: " },
            { "Token-Erneuerung fehlgeschlagen (HTTP ", "Token refresh failed (HTTP " },
            { "). Bitte `claude` starten und ggf. /login ausführen.", "). Start `claude` and run /login if needed." },
            { "Token-Erneuerung lieferte eine unerwartete Antwort.", "Token refresh returned an unexpected response." },
            { "Keine Organisation für diesen Session-Key gefunden.", "No organization found for this session key." },
            { "claude.ai blockiert die Anfrage (Cloudflare). Nutze besser die Claude-Code-Anmeldung.", "claude.ai blocked the request (Cloudflare). Prefer the Claude Code login." },
            { "Session-Key ungültig oder abgelaufen.", "Session key invalid or expired." },
            { "Keine Claude-Code-Anmeldung gefunden. Installiere Claude Code und führe `claude` → /login aus, oder hinterlege in den Einstellungen einen claude.ai-Session-Key.", "No Claude Code login found. Install Claude Code and run `claude` → /login, or set a claude.ai session key in Settings." },
            { "Die Claude-Code-Anmeldedaten enthalten kein OAuth-Token (API-Key-Login?).", "Claude Code credentials contain no OAuth token (API key login?)." },
            { "Kein Session-Key hinterlegt (Einstellungen).", "No session key configured (Settings)." },
            { "Claude-Code-Anmeldung abgelaufen. Bitte `claude` starten und /login ausführen.", "Claude Code login expired. Start `claude` and run /login." },
            { "Zu viele Anfragen – das Intervall wird automatisch verlängert.", "Rate limited – the refresh interval backs off automatically." },
            { "gerade zurückgesetzt", "just reset" },
            { "Reset in ", "Resets in " },
            { "Quelle: ", "Source: " },
            { "Noch keine Daten", "No data yet" },
            { "Aktualisiere …", "Refreshing …" },
            { "Lade Nutzungsdaten …", "Loading usage …" },
            { "Extra-Nutzung", "Extra usage" },
            { "Aktualisiert ", "Updated " },
            { "Einstellungen", "Settings" },
            { "Einstellungen …", "Settings …" },
            { "Aktualisieren", "Refresh" },
            { " frei", " left" },
            { "Claude Usage – Einstellungen", "Claude Usage – Settings" },
            { "Claude-Code-Anmeldung (empfohlen)", "Claude Code login (recommended)" },
            { "claude.ai-Session-Key", "claude.ai session key" },
            { "Browser → claude.ai → F12 → Anwendung → Cookies → Wert von „sessionKey“ (beginnt mit sk-ant-sid…). Wird mit Windows-DPAPI verschlüsselt gespeichert.", "Browser → claude.ai → F12 → Application → Cookies → value of \"sessionKey\" (starts with sk-ant-sid…). Stored encrypted with Windows DPAPI." },
            { "Verbleibend statt verbraucht anzeigen", "Show remaining instead of used" },
            { "Benachrichtigungen bei Schwellenwerten", "Notify at thresholds" },
            { "Benachrichtigen, wenn die Session zurückgesetzt wird", "Notify when the session resets" },
            { "Mit Windows starten", "Start with Windows" },
            { "Datenquelle", "Data source" },
            { "Intervall (Sek.)", "Interval (sec)" },
            { "Im Tray anzeigen", "Show in tray" },
            { "Hinweise", "Notifications" },
            { "Schwellen (%)", "Thresholds (%)" },
            { "System", "System" },
            { "Abbrechen", "Cancel" },
            { "Bitte einen Session-Key eingeben.", "Please enter a session key." },
            { "Session zurückgesetzt", "Session reset" },
            { "Dein 5-Stunden-Kontingent ist wieder frei.", "Your 5-hour allowance is available again." },
            { "{0}: {1} % erreicht", "{0}: {1} % reached" },
            { "Nutzung auf claude.ai öffnen", "Open usage on claude.ai" },
            { "Beenden", "Exit" },
            { "Fehler: ", "Error: " },
            { "Claude Token-Kalender", "Claude Token Calendar" },
            { "Token-Kalender …", "Token calendar …" },
            { "Kalender", "Calendar" },
            { "Heute", "Today" },
            { "Alle Tokens", "All tokens" },
            { "Ohne Cache-Lesen", "Excl. cache reads" },
            { "Lese Claude-Code-Verläufe …", "Reading Claude Code transcripts …" },
            { "Keine Claude-Code-Verläufe gefunden in ", "No Claude Code transcripts found in " },
            { "{0} Tokens im Monat  ·  {1} aktive Tage  ·  Ø {2} pro aktivem Tag", "{0} tokens this month  ·  {1} active days  ·  avg {2} per active day" },
            { "Weniger", "Less" },
            { "Mehr", "More" },
            { "Lade …", "Loading …" },
            { "An diesem Tag keine Nutzung.", "No usage on this day." },
            { "Tokens gesamt", "Total tokens" },
            { "Tokens ohne Cache-Lesen", "Tokens excl. cache reads" },
            { "Input", "Input" },
            { "Output", "Output" },
            { "Cache schreiben", "Cache writes" },
            { "Cache lesen", "Cache reads" },
            { "Antworten", "Responses" },
            { "Nach Modell", "By model" },
            { " Mrd.", " B" },
            { "Unerwartete Antwort der API (kein JSON-Objekt).", "Unexpected API response (not a JSON object)." },
        };

        public static string T(string german)
        {
            if (German) return german;
            string en;
            return En.TryGetValue(german, out en) ? en : german;
        }
    }
}
