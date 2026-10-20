using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace ClaudeUsageTracker
{
    /// Thin helpers over JavaScriptSerializer, which ships with .NET Framework
    /// and therefore needs no NuGet package.
    static class Json
    {
        static JavaScriptSerializer NewSerializer()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = 16 * 1024 * 1024;
            return s;
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            var obj = NewSerializer().DeserializeObject(json);
            return obj as Dictionary<string, object>;
        }

        public static string Serialize(object value)
        {
            return NewSerializer().Serialize(value);
        }

        public static T Deserialize<T>(string json)
        {
            return NewSerializer().Deserialize<T>(json);
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static IList Arr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as IList;
            return null;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return null;
        }

        /// The API has returned utilization as int, double and "42%" over time —
        /// mirror the original app's tolerant parser.
        public static double? Num(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return null;
            var s = v as string;
            if (s != null)
            {
                double parsed;
                if (double.TryParse(s.Trim().Replace("%", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) return parsed;
                return null;
            }
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return null; }
        }

        public static DateTime? Date(Dictionary<string, object> d, string key)
        {
            var s = Str(d, key);
            if (string.IsNullOrEmpty(s)) return null;
            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto)) return dto.UtcDateTime;
            return null;
        }
    }
}
