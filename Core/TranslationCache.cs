using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GlassTranslate.Core
{
    /// <summary>
    /// 持久化翻译缓存：归一化原文 → 中文译文。
    /// 内存字典 + JSON 落盘（3 秒防抖）。同一句子第二次出现零 API 成本。
    /// 结构稳定，后续可无痛升级为 SQLite，不影响调用方。
    /// </summary>
    public sealed class TranslationCache
    {
        private readonly Dictionary<string, string> _map = new();
        private readonly string _path;
        private readonly object _gate = new();
        private DateTime _lastFlush = DateTime.MinValue;
        private bool _dirty;

        public int Count { get { lock (_gate) return _map.Count; } }

        public TranslationCache(string path)
        {
            _path = path;
            try
            {
                if (File.Exists(_path))
                {
                    var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path));
                    if (data != null)
                        foreach (var kv in data) _map[kv.Key] = kv.Value;
                }
            }
            catch { /* 损坏则视为空缓存 */ }
        }

        public string? Get(string key) { lock (_gate) return _map.GetValueOrDefault(key); }
        public bool Has(string key) { lock (_gate) return _map.ContainsKey(key); }

        public void Put(string key, string value)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
            lock (_gate)
            {
                _map[key] = value;
                _dirty = true;
            }
            MaybeFlush();
        }

        public void Clear()
        {
            lock (_gate) { _map.Clear(); _dirty = true; }
            MaybeFlush();
        }

        public void Flush()
        {
            lock (_gate) { _dirty = true; _lastFlush = DateTime.MinValue; }
            MaybeFlush();
        }

        private void MaybeFlush()
        {
            bool flush = false;
            lock (_gate)
            {
                if (_dirty && (DateTime.UtcNow - _lastFlush).TotalSeconds > 3)
                {
                    flush = true;
                    _dirty = false;
                    _lastFlush = DateTime.UtcNow;
                }
            }
            if (!flush) return;
            try
            {
                Dictionary<string, string> snap;
                lock (_gate) snap = new(_map);
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, JsonSerializer.Serialize(snap));
            }
            catch { lock (_gate) _dirty = true; }
        }
    }
}
