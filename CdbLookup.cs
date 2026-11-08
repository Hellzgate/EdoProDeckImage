using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EdoProDeckImage
{
    /// <summary>
    /// 덱 이미지의 **이미지 폴백**용 cdb 조회 — 같은 카드의 alias(원본 ID)와 이름들.
    ///   · EDOPro 폴더(expansions\*.cdb · repositories\**\*.cdb)를 찾아 **필요할 때만** 연다.
    ///   · 결과는 &lt;data&gt;\cdb-cache.json 에 저장해 다음 실행부터 즉시 응답.
    ///   · 커스텀/프리릴리즈 카드는 EDOPro cdb 에만 있으므로, 이 정보가 없으면 이미지를 못 찾는다.
    /// </summary>
    public sealed class CdbLookup : IDisposable
    {
        public sealed class Entry
        {
            public int Alias;
            public readonly List<string> Names = new List<string>();
        }

        private sealed class CacheFile
        {
            public int Version { get; set; } = 1;
            public Dictionary<string, CacheEntry> Entries { get; set; } = new Dictionary<string, CacheEntry>();
        }

        private sealed class CacheEntry
        {
            public int Alias { get; set; }
            public List<string> Names { get; set; } = new List<string>();
        }

        private readonly List<string> _files = new List<string>();
        private readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>();
        private readonly object _lock = new object();
        private readonly string _cachePath;
        private bool _dirty;

        private CdbLookup(string cachePath) { _cachePath = cachePath ?? ""; }

        /// <summary>찾은 cdb 개수(0 이면 alias/이름 폴백 불가).</summary>
        public int CdbCount { get { return _files.Count; } }

        public static CdbLookup Create(IEnumerable<string> picsFolders, string ydkPath, string cacheFolder)
        {
            string cachePath = "";
            try
            {
                if (!string.IsNullOrEmpty(cacheFolder))
                {
                    Directory.CreateDirectory(cacheFolder);
                    cachePath = Path.Combine(cacheFolder, "cdb-cache.json");
                }
            }
            catch { cachePath = ""; }

            var me = new CdbLookup(cachePath);
            me.LoadCache();
            try { me._files.AddRange(FindCdbFiles(picsFolders, ydkPath)); } catch { }
            return me;
        }

        // ── cdb 파일 찾기 ────────────────────────────────────────
        /// <summary>EDOPro 설치 폴더를 추정해 실제로 존재하는 cdb 만 모은다.</summary>
        public static List<string> FindCdbFiles(IEnumerable<string> picsFolders, string ydkPath)
        {
            var roots = new List<string>();

            Action<string> addRoot = delegate (string dir)
            {
                if (string.IsNullOrEmpty(dir)) return;
                string full;
                try { full = Path.GetFullPath(dir); } catch { return; }
                if (!Directory.Exists(full)) return;
                // EDOPro 폴더처럼 보이는 곳만(expansions/repositories 하위 폴더가 있는 곳)
                bool looks = false;
                try
                {
                    looks = Directory.Exists(Path.Combine(full, "expansions"))
                         || Directory.Exists(Path.Combine(full, "repositories"))
                         || Directory.Exists(Path.Combine(full, "WindBot"));
                }
                catch { }
                if (!looks) return;
                foreach (string r in roots)
                    if (string.Equals(r, full, StringComparison.OrdinalIgnoreCase)) return;
                roots.Add(full);
            };

            // ① pics 폴더와 그 부모(=<EDOPro>\pics → <EDOPro>)
            if (picsFolders != null)
            {
                foreach (string p in picsFolders)
                {
                    if (string.IsNullOrEmpty(p) || !Directory.Exists(p)) continue;
                    addRoot(p);
                    try { addRoot(Path.GetDirectoryName(Path.GetFullPath(p))); } catch { }
                }
            }

            // ② .ydk 위로 6단계
            if (!string.IsNullOrEmpty(ydkPath))
            {
                try
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(ydkPath));
                    for (int i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
                    {
                        addRoot(dir);
                        string up = Path.GetDirectoryName(dir);
                        if (string.IsNullOrEmpty(up) || up == dir) break;
                        dir = up;
                    }
                }
                catch { }
            }

            // ③ exe 위치와 위로 3단계
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
                {
                    addRoot(dir);
                    string up = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(up) || up == dir) break;
                    dir = up;
                }
            }
            catch { }

            // ④ 흔한 설치 경로
            string[] known = { @"D:\edopro", @"C:\ProjectIgnis", @"D:\ProjectIgnis", @"C:\edopro", @"D:\EDOPro" };
            foreach (string k in known) addRoot(k);

            var files = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ⑤ exe 옆에 내장한 카드 DB (EDOPro 없는 PC 대응)
            try
            {
                Collect(files, seen, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "carddata"), false);
            }
            catch { }

            foreach (string root in roots)
            {
                Collect(files, seen, Path.Combine(root, "expansions"), false);
                Collect(files, seen, Path.Combine(root, "repositories"), true);
                if (files.Count >= 500) break;
            }
            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static void Collect(List<string> files, HashSet<string> seen, string dir, bool recursive)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*.cdb",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                {
                    if (seen.Add(f)) files.Add(f);
                }
            }
            catch { }
        }

        // ── 조회 ────────────────────────────────────────────────
        /// <summary>캐시에 있으면 바로, 없으면 null(그 상태로 두고 Prefetch 로 채운다).</summary>
        public Entry Get(int id)
        {
            lock (_lock)
            {
                Entry e;
                return _cache.TryGetValue(id, out e) ? e : null;
            }
        }

        /// <summary>여러 카드를 **cdb 마다 한 번씩** 조회해 채운다(없는 카드도 빈 항목으로 기억).</summary>
        public void Prefetch(IEnumerable<int> ids)
        {
            if (ids == null || _files.Count == 0) return;
            var want = new List<int>();
            lock (_lock)
            {
                foreach (int id in ids)
                {
                    if (id <= 0 || _cache.ContainsKey(id) || want.Contains(id)) continue;
                    want.Add(id);
                }
            }
            if (want.Count == 0) return;

            var found = new Dictionary<int, Entry>();
            string[] paramNames = new string[want.Count];
            for (int i = 0; i < want.Count; i++) paramNames[i] = "$p" + i;

            foreach (string file in _files)
            {
                try
                {
                    var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                    {
                        DataSource = file,
                        Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly
                    };
                    using (var conn = new Microsoft.Data.Sqlite.SqliteConnection(csb.ToString()))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText =
                                "SELECT t.id, t.name, IFNULL(d.alias,0) FROM texts t " +
                                "LEFT JOIN datas d ON d.id = t.id WHERE t.id IN (" +
                                string.Join(",", paramNames) + ")";
                            for (int i = 0; i < want.Count; i++)
                                cmd.Parameters.AddWithValue(paramNames[i], want[i]);

                            using (var rd = cmd.ExecuteReader())
                            {
                                while (rd.Read())
                                {
                                    int id = rd.GetInt32(0);
                                    Entry e;
                                    if (!found.TryGetValue(id, out e)) { e = new Entry(); found[id] = e; }
                                    if (!rd.IsDBNull(1))
                                    {
                                        string n = rd.GetString(1);
                                        if (!string.IsNullOrEmpty(n) && !e.Names.Contains(n)) e.Names.Add(n);
                                    }
                                    if (!rd.IsDBNull(2))
                                    {
                                        int al;
                                        try { al = rd.GetInt32(2); } catch { al = 0; }
                                        if (al > 0 && e.Alias <= 0) e.Alias = al;
                                    }
                                }
                            }
                        }
                    }
                }
                catch { /* sqlite 없음/스키마 다름 등 → 그 cdb 는 건너뛴다 */ }
            }

            lock (_lock)
            {
                foreach (int id in want)
                {
                    Entry e;
                    if (found.TryGetValue(id, out e)) _cache[id] = e;
                    else if (!_cache.ContainsKey(id)) _cache[id] = new Entry();
                }
                _dirty = true;
            }
            Save();
        }

        /// <summary>이미지 검색용 이름 — **영문(비한글)만** (해외 사이트 검색용).</summary>
        public List<string> EnglishNames(int id, bool excludeHangul)
        {
            var list = new List<string>();
            Entry e = Get(id);
            if (e == null) return list;
            lock (_lock)
            {
                foreach (string n in e.Names)
                {
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    if (n.Length < 3) continue;
                    if (excludeHangul && HasHangul(n)) continue;
                    if (!list.Contains(n)) list.Add(n);
                }
            }
            return list;
        }

        /// <summary>표시용 이름(한국어 우선).</summary>
        public string DisplayName(int id)
        {
            Entry e = Get(id);
            if (e == null) return "";
            lock (_lock)
            {
                string korean = null;
                foreach (string n in e.Names)
                {
                    if (string.IsNullOrWhiteSpace(n)) continue;
                    if (HasHangul(n)) return n;
                    if (korean == null) korean = n;
                }
                return korean ?? "";
            }
        }

        public int AliasOf(int id)
        {
            Entry e = Get(id);
            return e == null ? 0 : e.Alias;
        }

        public static bool HasHangul(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s)
                if (c >= 0xAC00 && c <= 0xD7A3) return true;
            return false;
        }

        // ── 캐시 ────────────────────────────────────────────────
        private void LoadCache()
        {
            if (_cachePath.Length == 0 || !File.Exists(_cachePath)) return;
            try
            {
                string json = File.ReadAllText(_cachePath);
                CacheFile c = JsonSerializer.Deserialize<CacheFile>(json);
                if (c == null || c.Version != 1 || c.Entries == null) return;
                lock (_lock)
                {
                    foreach (KeyValuePair<string, CacheEntry> kv in c.Entries)
                    {
                        int id;
                        if (!int.TryParse(kv.Key, out id)) continue;
                        var e = new Entry();
                        e.Alias = kv.Value.Alias;
                        if (kv.Value.Names != null)
                            foreach (string n in kv.Value.Names)
                                if (!string.IsNullOrEmpty(n) && !e.Names.Contains(n)) e.Names.Add(n);
                        _cache[id] = e;
                    }
                }
            }
            catch { /* 깨진 캐시는 무시하고 다시 만든다 */ }
        }

        public void Save()
        {
            if (_cachePath.Length == 0) return;
            bool dirty;
            CacheFile c = null;
            lock (_lock)
            {
                dirty = _dirty;
                if (dirty)
                {
                    c = new CacheFile();
                    foreach (KeyValuePair<int, Entry> kv in _cache)
                    {
                        var ce = new CacheEntry { Alias = kv.Value.Alias };
                        ce.Names.AddRange(kv.Value.Names);
                        c.Entries[kv.Key.ToString()] = ce;
                    }
                }
            }
            if (!dirty) return;
            try
            {
                string json = JsonSerializer.Serialize(c, new JsonSerializerOptions { WriteIndented = false });
                string tmp = _cachePath + ".tmp";
                File.WriteAllText(tmp, json, System.Text.Encoding.UTF8);
                if (File.Exists(_cachePath)) File.Delete(_cachePath);
                File.Move(tmp, _cachePath);
                lock (_lock) { _dirty = false; }
            }
            catch { }
        }

        public void Dispose() { Save(); }
    }
}
