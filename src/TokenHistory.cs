using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeUsageTracker
{
    class DayUsage
    {
        public long Input, Output, CacheWrite, CacheRead;
        public int Messages;
        public readonly Dictionary<string, long> ByModel = new Dictionary<string, long>();

        public long Total(bool includeCacheReads)
        {
            return Input + Output + CacheWrite + (includeCacheReads ? CacheRead : 0);
        }
    }

    class TokenHistoryResult
    {
        public Dictionary<DateTime, DayUsage> Days = new Dictionary<DateTime, DayUsage>();
        public int Files;
        public int Messages;
    }

    /// Builds a per-day token history from Claude Code's local transcripts
    /// (%USERPROFILE%\.claude\projects\**\*.jsonl). The usage API only knows the
    /// current windows, so this is the only source for historical numbers.
    static class TokenHistory
    {
        static readonly Regex TimestampRx = new Regex("\"timestamp\":\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex ModelRx = new Regex("\"model\":\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex MessageIdRx = new Regex("\"id\":\"(msg_[^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex RequestIdRx = new Regex("\"requestId\":\"([^\"]+)\"", RegexOptions.Compiled);

        class Entry
        {
            public DateTime Day;
            public string Model;
            public long Input, Output, CacheWrite, CacheRead;
        }

        public static string ProjectsDir
        {
            get
            {
                var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
                return Path.Combine(dir, "projects");
            }
        }

        /// Blocking — call from a background thread.
        public static TokenHistoryResult Load()
        {
            var result = new TokenHistoryResult();
            if (!Directory.Exists(ProjectsDir)) return result;

            // Claude Code writes one line per content block, each repeating the same
            // usage object; resumed sessions also copy earlier messages into new files.
            // Dedupe on message id + request id, as ccusage does.
            var unique = new Dictionary<string, Entry>();
            var anonymous = new List<Entry>();

            foreach (var file in Directory.EnumerateFiles(ProjectsDir, "*.jsonl", SearchOption.AllDirectories))
            {
                result.Files++;
                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string key;
                            var e = ParseLine(line, out key);
                            if (e == null) continue;
                            if (key == null) { anonymous.Add(e); continue; }
                            Entry existing;
                            if (!unique.TryGetValue(key, out existing) || e.Output > existing.Output) unique[key] = e;
                        }
                    }
                }
                catch (IOException) { /* file being rotated; skip it this time */ }
            }

            foreach (var e in unique.Values) Add(result, e);
            foreach (var e in anonymous) Add(result, e);
            return result;
        }

        static void Add(TokenHistoryResult r, Entry e)
        {
            DayUsage d;
            if (!r.Days.TryGetValue(e.Day, out d)) r.Days[e.Day] = d = new DayUsage();
            d.Input += e.Input;
            d.Output += e.Output;
            d.CacheWrite += e.CacheWrite;
            d.CacheRead += e.CacheRead;
            d.Messages++;
            long m;
            d.ByModel.TryGetValue(e.Model, out m);
            d.ByModel[e.Model] = m + e.Input + e.Output + e.CacheWrite + e.CacheRead;
            r.Messages++;
        }

        /// Cheap targeted parse: lines can be megabytes long (tool output), so only the
        /// small usage object is handed to the JSON parser.
        static Entry ParseLine(string line, out string key)
        {
            key = null;
            if (line.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0) return null;
            var u = line.IndexOf("\"usage\":{", StringComparison.Ordinal);
            if (u < 0) return null;
            var start = u + 8;
            var end = MatchBrace(line, start);
            if (end < 0) return null;

            var usage = Json.ParseObject(line.Substring(start, end - start + 1));
            if (usage == null) return null;

            var model = Group(ModelRx, line, false) ?? "unknown";
            if (model == "<synthetic>") return null;     // locally generated, not billed

            // The top-level timestamp is written after the message object.
            var ts = Group(TimestampRx, line, true);
            DateTimeOffset when;
            if (ts == null || !DateTimeOffset.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out when)) return null;

            var e = new Entry
            {
                Day = when.ToLocalTime().Date,
                Model = model,
                Input = (long)(Json.Num(usage, "input_tokens") ?? 0),
                Output = (long)(Json.Num(usage, "output_tokens") ?? 0),
                CacheWrite = (long)(Json.Num(usage, "cache_creation_input_tokens") ?? 0),
                CacheRead = (long)(Json.Num(usage, "cache_read_input_tokens") ?? 0)
            };
            if (e.Input + e.Output + e.CacheWrite + e.CacheRead == 0) return null;

            var msgId = Group(MessageIdRx, line, false);
            var reqId = Group(RequestIdRx, line, false);
            if (msgId != null) key = msgId + ":" + (reqId ?? "");
            return e;
        }

        static string Group(Regex rx, string s, bool last)
        {
            if (!last)
            {
                var m = rx.Match(s);
                return m.Success ? m.Groups[1].Value : null;
            }
            string value = null;
            foreach (Match m in rx.Matches(s)) value = m.Groups[1].Value;
            return value;
        }

        /// Index of the brace closing the one at `open`, skipping over JSON strings.
        static int MatchBrace(string s, int open)
        {
            int depth = 0;
            bool inString = false;
            for (int i = open; i < s.Length; i++)
            {
                var c = s[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                }
                else if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i;
            }
            return -1;
        }
    }
}
