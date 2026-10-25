using System.IO;
using System.Text.Json;
using System.Threading;

namespace GlassTranslate.Core
{
    /// <summary>
    /// 轻量用量统计（进程内累加，定期/退出时落盘 usage.json）。
    /// 缓存命中数由 TranslationCache 单独统计（会话级）。
    /// </summary>
    public static class UsageStats
    {
        private static readonly object Gate = new();
        private static long _apiCalls, _linesTranslated;
        private static bool _loaded;

        private static string Path => System.IO.Path.Combine(
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "GlassTranslate"), "usage.json");

        public static long CacheHits { get; internal set; }

        public static void AddApiCall()
        {
            lock (Gate) { Load(); _apiCalls++; MaybeSave(); }
        }

        public static void AddLine()
        {
            lock (Gate) { Load(); _linesTranslated++; }
        }

        public static (long ApiCalls, long Lines, long CacheHitsThisSession) Snapshot()
        {
            lock (Gate)
            {
                Load();
                return (_apiCalls, _linesTranslated, CacheHits);
            }
        }

        public static void Flush()
        {
            lock (Gate)
            {
                Load();
                Save();
            }
        }

        private static void MaybeSave()
        {
            // 每 50 次 API 调用落盘一次，其余靠退出时 Flush
            if (_apiCalls % 50 == 0) Save();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(Path))
                {
                    var doc = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path));
                    _apiCalls = doc.GetProperty("api").GetInt64();
                    _linesTranslated = doc.GetProperty("lines").GetInt64();
                }
            }
            catch { }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.WriteAllText(Path, JsonSerializer.Serialize(new { api = _apiCalls, lines = _linesTranslated }));
            }
            catch { }
        }
    }
}
