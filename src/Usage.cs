using System;
using System.Collections;
using System.Collections.Generic;

namespace ClaudeUsageTracker
{
    enum StatusLevel { Safe, Moderate, Critical }

    class UsageWindow
    {
        public string Name { get; set; }
        public double Percent { get; set; }        // 0-100, always "used"
        public DateTime? ResetsAtUtc { get; set; }
        public TimeSpan Duration { get; set; }

        public double DisplayPercent(bool showRemaining)
        {
            return showRemaining ? Math.Max(0, 100 - Percent) : Percent;
        }

        /// Fraction (0..1) of the window that has already elapsed, or null if unknown.
        public double? ElapsedFraction()
        {
            if (!ResetsAtUtc.HasValue || Duration <= TimeSpan.Zero) return null;
            var remaining = ResetsAtUtc.Value - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return 1.0;
            var elapsed = Duration - remaining;
            return Math.Min(Math.Max(elapsed.TotalSeconds / Duration.TotalSeconds, 0), 1);
        }

        /// Port of UsageStatusCalculator.calculateStatus: once 15% of the window has
        /// passed, color by projected end-of-window usage instead of raw usage.
        public StatusLevel Status()
        {
            var u = Percent / 100.0;
            var t = ElapsedFraction();
            if (t.HasValue && t.Value >= 0.15 && t.Value < 1.0 && u > 0)
            {
                var projected = u / t.Value;
                if (projected < 0.70) return StatusLevel.Safe;
                if (projected < 0.90) return StatusLevel.Moderate;
                return StatusLevel.Critical;
            }
            if (Percent < 70) return StatusLevel.Safe;
            if (Percent < 90) return StatusLevel.Moderate;
            return StatusLevel.Critical;
        }
    }

    class UsageSnapshot
    {
        public UsageWindow Session { get; set; }
        public UsageWindow Weekly { get; set; }
        public List<UsageWindow> Models { get; set; }
        public double? ExtraUsed { get; set; }
        public double? ExtraLimit { get; set; }
        public string ExtraCurrency { get; set; }
        public string Plan { get; set; }
        public string Source { get; set; }
        public DateTime FetchedAtUtc { get; set; }

        public UsageSnapshot()
        {
            Models = new List<UsageWindow>();
            FetchedAtUtc = DateTime.UtcNow;
        }
    }

    static class UsageParser
    {
        static readonly TimeSpan FiveHours = TimeSpan.FromHours(5);
        static readonly TimeSpan SevenDays = TimeSpan.FromDays(7);

        /// Parses the body of claude.ai/api/organizations/{id}/usage and
        /// api.anthropic.com/api/oauth/usage — both share the same shape.
        public static UsageSnapshot Parse(string body)
        {
            var json = Json.ParseObject(body);
            if (json == null) throw new UsageException("Unerwartete Antwort der API (kein JSON-Objekt).");

            var snap = new UsageSnapshot();
            snap.Session = Window(json, "five_hour", L.T("Session (5 h)"), FiveHours)
                ?? new UsageWindow { Name = L.T("Session (5 h)"), Duration = FiveHours };
            snap.Weekly = Window(json, "seven_day", L.T("Woche (alle Modelle)"), SevenDays)
                ?? new UsageWindow { Name = L.T("Woche (alle Modelle)"), Duration = SevenDays };

            // Legacy per-model fields. "omelette" is the internal name for Claude Design.
            var models = new Dictionary<string, UsageWindow>();
            AddModel(models, "opus", Window(json, "seven_day_opus", "Opus", SevenDays));
            AddModel(models, "sonnet", Window(json, "seven_day_sonnet", "Sonnet", SevenDays));
            AddModel(models, "fable", Window(json, "seven_day_fable", "Fable", SevenDays));
            AddModel(models, "design", Window(json, "seven_day_omelette", "Design", SevenDays));

            // Newer responses null the legacy fields and report a limits[] array instead;
            // when present it is the source of truth.
            var limits = Json.Arr(json, "limits");
            if (limits != null)
            {
                foreach (var item in limits)
                {
                    var limit = item as Dictionary<string, object>;
                    if (limit == null || Json.Str(limit, "kind") != "weekly_scoped") continue;
                    var model = Json.Obj(Json.Obj(limit, "scope"), "model");
                    var percent = Json.Num(limit, "percent");
                    if (model == null || !percent.HasValue) continue;

                    var id = (Json.Str(model, "id") ?? "").ToLowerInvariant();
                    var display = Json.Str(model, "display_name") ?? "";
                    var name = display.ToLowerInvariant();
                    string key = null;
                    if (Matches(id, name, "fable", "mythos")) key = "fable";
                    else if (Matches(id, name, "opus")) key = "opus";
                    else if (Matches(id, name, "sonnet")) key = "sonnet";
                    else if (Matches(id, name, "design", "omelette")) key = "design";
                    if (key == null) key = name.Length > 0 ? name : id;
                    if (key.Length == 0) continue;

                    UsageWindow existing;
                    models.TryGetValue(key, out existing);
                    models[key] = new UsageWindow
                    {
                        Name = display.Length > 0 ? display : (existing != null ? existing.Name : key),
                        Percent = percent.Value,
                        ResetsAtUtc = Json.Date(limit, "resets_at") ?? (existing != null ? existing.ResetsAtUtc : null),
                        Duration = SevenDays
                    };
                }
            }
            snap.Models.AddRange(models.Values);

            var extra = Json.Obj(json, "extra_usage");
            if (extra != null && Json.Str(extra, "is_enabled") == "True")
            {
                // Credits are reported in cents.
                var used = Json.Num(extra, "used_credits");
                var limitValue = Json.Num(extra, "monthly_limit");
                if (used.HasValue) snap.ExtraUsed = used.Value / 100.0;
                if (limitValue.HasValue) snap.ExtraLimit = limitValue.Value / 100.0;
                snap.ExtraCurrency = Json.Str(extra, "currency");
            }
            return snap;
        }

        static bool Matches(string id, string name, params string[] keys)
        {
            foreach (var k in keys)
                if (name == k || id.Contains(k)) return true;
            return false;
        }

        static void AddModel(Dictionary<string, UsageWindow> models, string key, UsageWindow w)
        {
            if (w != null) models[key] = w;
        }

        static UsageWindow Window(Dictionary<string, object> json, string key, string name, TimeSpan duration)
        {
            var o = Json.Obj(json, key);
            if (o == null) return null;
            var util = Json.Num(o, "utilization");
            if (!util.HasValue) return null;
            var w = new UsageWindow
            {
                Name = name,
                Percent = util.Value,
                ResetsAtUtc = Json.Date(o, "resets_at"),
                Duration = duration
            };
            // A window whose reset time has passed has rolled over already.
            if (w.ResetsAtUtc.HasValue && w.ResetsAtUtc.Value < DateTime.UtcNow) w.Percent = 0;
            return w;
        }
    }

    class UsageException : Exception
    {
        public bool NeedsUserAction { get; private set; }
        public UsageException(string message) : base(message) { }
        public UsageException(string message, bool needsUserAction) : base(message) { NeedsUserAction = needsUserAction; }
    }
}
