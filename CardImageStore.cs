using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EdoProDeckImage
{
    /// <summary>
    /// 카드 이미지 캐시. 렌더 사이에 재사용하고, 없으면 (옵션) 인터넷에서 받아온다.
    ///
    /// 이미지 결정 순서
    ///   ① 로컬 pics 폴더(여러 개) — &lt;카드ID&gt;.jpg / .png / .jpeg
    ///   ② 캐시 폴더(&lt;data&gt;\imgcache)
    ///   ③ 같은 ID       : YGOPRODeck cards → cards_small → cards_cropped
    ///   ④ cdb alias     : 재록·다른 일러의 **원본 ID** 로 ③ 과 같은 시도
    ///   ⑤ cdb 영문 이름 : YGOPRODeck 이름 검색 → 그 카드 이미지
    ///   ⑥ Yugipedia     : 문서 대표 이미지(마지막 수단)
    ///   실패는 그 실행에서만 기억하고, 로컬 pics 가 있으면 경고하지 않는다.
    /// </summary>
    public sealed class CardImageStore : IDisposable
    {
        private static readonly string[] UrlTemplates =
        {
            "https://images.ygoprodeck.com/images/cards/{0}.jpg",
            "https://images.ygoprodeck.com/images/cards_small/{0}.jpg",
            "https://images.ygoprodeck.com/images/cards_cropped/{0}.jpg"
        };

        private static readonly string[] Extensions = { ".jpg", ".png", ".jpeg" };

        private readonly List<string> _picsFolders = new List<string>();
        private readonly bool _allowNetwork;
        private readonly string _cacheDir;
        private readonly CdbLookup _cdb;

        private readonly Dictionary<int, Image> _map = new Dictionary<int, Image>();
        private readonly HashSet<int> _tried = new HashSet<int>();
        private readonly object _gate = new object();

        private int _fromNetwork;
        private int _fromCache;
        private int _attempts;
        private int _fromAlias;
        private int _fromName;
        private int _fromYugipedia;

        public CardImageStore(string picsFolder, bool allowNetwork, string cacheDir)
            : this(SplitFolders(picsFolder), allowNetwork, cacheDir, null) { }

        public CardImageStore(IEnumerable<string> picsFolders, bool allowNetwork, string cacheDir, CdbLookup cdb)
        {
            if (picsFolders != null)
            {
                foreach (string f in picsFolders)
                {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    string full;
                    try { full = Path.GetFullPath(f); } catch { continue; }
                    bool dup = false;
                    foreach (string s in _picsFolders)
                        if (string.Equals(s, full, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                    if (!dup) _picsFolders.Add(full);
                }
            }
            _allowNetwork = allowNetwork;
            _cacheDir = cacheDir ?? "";
            _cdb = cdb;
            if (_allowNetwork && _cacheDir.Length > 0)
            {
                try { Directory.CreateDirectory(_cacheDir); } catch { }
            }
        }

        public static List<string> SplitFolders(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (string part in text.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = part.Trim().Trim('"');
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }

        public int FromNetwork { get { return _fromNetwork; } }
        public int Attempts { get { return _attempts; } }
        /// <summary>캐시 파일에서 읽은 장수(이미 받아 둔 것).</summary>
        public int FromCache { get { return _fromCache; } }
        public bool HasNetwork { get { return _allowNetwork; } }
        public int Count { get { lock (_gate) return _map.Count; } }
        /// <summary>로컬 pics 폴더 개수(자동 탐색 포함).</summary>
        public int FolderCount { get { return _picsFolders.Count; } }
        /// <summary>cdb 조회가 가능한가(alias·이름 폴백).</summary>
        public bool HasCdb { get { return _cdb != null && _cdb.CdbCount > 0; } }
        public int CdbCount { get { return _cdb == null ? 0 : _cdb.CdbCount; } }
        public int FromAlias { get { return _fromAlias; } }
        public int FromName { get { return _fromName; } }
        public int FromYugipedia { get { return _fromYugipedia; } }

        /// <summary>표시용 카드 이름(cdb) — 없으면 "".</summary>
        public string NameOf(int cardId)
        {
            if (_cdb == null) return "";
            try { return _cdb.DisplayName(cardId); } catch { return ""; }
        }

        /// <summary>cdb 를 미리 읽어 alias/이름을 채운다(네트워크 시도 전에 호출).</summary>
        public void Prefetch(IEnumerable<int> ids)
        {
            if (_cdb == null || ids == null) return;
            try { _cdb.Prefetch(ids); } catch { }
        }

        /// <summary>렌더용 — 캐시/로컬만(인터넷 대기 없음).</summary>
        public Image Get(int cardId) { return Lookup(cardId, false); }

        /// <summary>프리로드용 — 없으면 인터넷까지 시도.</summary>
        public Image GetOrFetch(int cardId) { return Lookup(cardId, true); }

        private Image Lookup(int cardId, bool allowFetch)
        {
            lock (_gate)
            {
                Image cached;
                if (_map.TryGetValue(cardId, out cached)) return cached;
            }

            Image img = LoadLocal(cardId);
            if (img != null) return Put(cardId, img);

            if (allowFetch && _allowNetwork)
            {
                Interlocked.Increment(ref _attempts);
                bool downloaded;
                img = LoadNetwork(cardId, out downloaded);
                if (img != null)
                {
                    if (downloaded) Interlocked.Increment(ref _fromNetwork);
                    else Interlocked.Increment(ref _fromCache);
                    return Put(cardId, img);
                }
            }
            else if (_allowNetwork && IsDownloadPending(cardId))
            {
                // 아직 받아보지 않았으면 "없음"으로 확정하지 않는다
                return null;
            }

            return Put(cardId, null);
        }

        private bool IsDownloadPending(int cardId)
        {
            lock (_tried) return !_tried.Contains(cardId);
        }

        private Image Put(int cardId, Image img)
        {
            lock (_gate)
            {
                Image cur;
                if (_map.TryGetValue(cardId, out cur))
                {
                    if (cur != null && !ReferenceEquals(cur, img) && img != null) img.Dispose();
                    return cur;
                }
                _map[cardId] = img;
                return img;
            }
        }

        private Image LoadLocal(int cardId)
        {
            foreach (string dir in _picsFolders)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string ext in Extensions)
                {
                    string p = Path.Combine(dir, cardId.ToString() + ext);
                    if (!File.Exists(p)) continue;
                    try { return Image.FromFile(p); } catch { }
                }
            }
            return null;
        }

        private bool HasLocal(int cardId)
        {
            foreach (string dir in _picsFolders)
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string ext in Extensions)
                    if (File.Exists(Path.Combine(dir, cardId.ToString() + ext))) return true;
            }
            return false;
        }

        private Image LoadNetwork(int cardId, out bool downloaded)
        {
            downloaded = false;
            if (_cacheDir.Length == 0) return null;

            string cached = Path.Combine(_cacheDir, cardId.ToString() + ".jpg");
            if (File.Exists(cached))
            {
                try { return Image.FromFile(cached); } catch { }
            }

            lock (_tried)
            {
                if (_tried.Contains(cardId)) return null;
                _tried.Add(cardId);
            }

            try
            {
                // ③ 같은 ID, ④ cdb alias(원본 ID)
                foreach (int id in ImageIds(cardId))
                {
                    if (DownloadById(cached, id))
                    {
                        if (id != cardId) Interlocked.Increment(ref _fromAlias);
                        downloaded = true;
                        return LoadFile(cached);
                    }
                }

                // ⑤ cdb 영문 이름 → YGOPRODeck 검색
                foreach (string name in ImageNames(cardId))
                {
                    int found = SearchYgoProDeckId(name);
                    if (found > 0 && found != cardId && DownloadById(cached, found))
                    {
                        Interlocked.Increment(ref _fromName);
                        downloaded = true;
                        return LoadFile(cached);
                    }
                }

                // ⑥ Yugipedia 문서 이미지
                foreach (string name in ImageNames(cardId))
                {
                    string url = SearchYugipediaImage(name);
                    if (!string.IsNullOrEmpty(url) && DownloadUrl(cached, url))
                    {
                        Interlocked.Increment(ref _fromYugipedia);
                        downloaded = true;
                        return LoadFile(cached);
                    }
                }
            }
            catch
            {
                try { File.Delete(cached); } catch { }
                return null;
            }

            try { File.Delete(cached); } catch { }
            return null;
        }

        private static Image LoadFile(string path)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var img = Image.FromStream(fs))
                    return (Image)img.Clone();      // 스트림 의존 제거
            }
            catch { return null; }
        }

        /// <summary>이미지 후보 ID — 자기 자신 + cdb alias(원본).</summary>
        private List<int> ImageIds(int cardId)
        {
            var ids = new List<int> { cardId };
            int alias = _cdb == null ? 0 : _cdb.AliasOf(cardId);
            if (alias > 0 && !ids.Contains(alias)) ids.Add(alias);
            return ids;
        }

        /// <summary>이미지 검색용 이름 — cdb 의 **영문(비한글)** 이름만.</summary>
        private List<string> ImageNames(int cardId)
        {
            return _cdb == null ? new List<string>() : _cdb.EnglishNames(cardId, true);
        }

        private static bool DownloadById(string dst, int id)
        {
            foreach (string t in UrlTemplates)
                if (DownloadUrl(dst, string.Format(t, id))) return true;
            return false;
        }

        /// <summary>URL 하나를 받아 저장. 404 는 즉시 포기, 그 외는 3회 재시도. HTML 오류 페이지는 거른다.</summary>
        private static bool DownloadUrl(string dst, string url)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 12000;
                    req.ReadWriteTimeout = 12000;
                    req.UserAgent = "EdoProDeckImage/1.3 (+https://github.com/Hellzgate/EdoProDeckImage)";

                    byte[] bytes;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                    if (bytes == null || bytes.Length <= 512) throw new Exception("empty image");
                    if (bytes[0] == (byte)'<') throw new Exception("html error page");

                    string tmp = dst + ".tmp";
                    File.WriteAllBytes(tmp, bytes);
                    try { if (File.Exists(dst)) File.Delete(dst); } catch { }
                    File.Move(tmp, dst);
                    return true;
                }
                catch (WebException wex)
                {
                    var http = wex.Response as HttpWebResponse;
                    if (http != null && (int)http.StatusCode == 404) return false;   // 다음 후보
                    if (attempt < 2) Thread.Sleep(300 * (attempt + 1));
                }
                catch
                {
                    if (attempt < 2) Thread.Sleep(300 * (attempt + 1));
                }
            }
            return false;
        }

        // ── 이름 검색(YGOPRODeck / Yugipedia) ────────────────────
        private static readonly Dictionary<string, int> _nameIdCache = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _yugiCache = new Dictionary<string, string>();
        private static readonly object _lookupLock = new object();

        private static string HttpGetString(string url, int timeoutMs)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = timeoutMs;
                    req.UserAgent = "EdoProDeckImage/1.3 (+https://github.com/Hellzgate/EdoProDeckImage)";
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (var sr = new StreamReader(s, Encoding.UTF8))
                        return sr.ReadToEnd();
                }
                catch
                {
                    if (attempt == 0) Thread.Sleep(250);
                }
            }
            return null;
        }

        private static int SearchYgoProDeckId(string name)
        {
            lock (_lookupLock)
            {
                int v;
                if (_nameIdCache.TryGetValue(name, out v)) return v;
            }
            int found = 0;
            try
            {
                string url = "https://db.ygoprodeck.com/api/v7/cardinfo.php?fname=" +
                             Uri.EscapeDataString(name) + "&num=1&offset=0";
                string json = HttpGetString(url, 12000);
                if (!string.IsNullOrEmpty(json))
                {
                    var m = Regex.Match(json, "\"id\"\\s*:\\s*(\\d+)");
                    if (m.Success) int.TryParse(m.Groups[1].Value, out found);
                }
            }
            catch { }
            lock (_lookupLock) { _nameIdCache[name] = found; }
            return found;
        }

        private static string SearchYugipediaImage(string name)
        {
            lock (_lookupLock)
            {
                string v;
                if (_yugiCache.TryGetValue(name, out v)) return v;
            }
            string found = null;
            try
            {
                string url = "https://yugipedia.com/api.php?action=query&titles=" + Uri.EscapeDataString(name)
                           + "&prop=pageimages&piprop=original&redirects=1&format=json";
                string json = HttpGetString(url, 12000);
                var m = Regex.Match(json ?? "", "\"source\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success)
                {
                    found = m.Groups[1].Value.Replace("\\/", "/");
                }
                else
                {
                    string url2 = "https://yugipedia.com/api.php?action=query&generator=search&gsrsearch="
                                + Uri.EscapeDataString(name) + "&gsrlimit=1&prop=pageimages&piprop=original&format=json";
                    json = HttpGetString(url2, 12000);
                    m = Regex.Match(json ?? "", "\"source\"\\s*:\\s*\"([^\"]+)\"");
                    if (m.Success) found = m.Groups[1].Value.Replace("\\/", "/");
                }
            }
            catch { }
            lock (_lookupLock) { _yugiCache[name] = found; }
            return found;
        }

        /// <summary>여러 장 미리 읽기(4 병렬). progress(완료, 전체)</summary>
        public int Preload(IEnumerable<int> ids, Action<int, int> progress)
        {
            if (ids == null) return 0;
            var ordered = new List<int>();
            foreach (int id in ids) if (!ordered.Contains(id)) ordered.Add(id);

            // alias/이름 폴백에 필요한 cdb 정보를 먼저 채운다
            Prefetch(ordered);

            var list = new List<int>();
            foreach (int id in ordered)
            {
                lock (_gate) { if (_map.ContainsKey(id)) continue; }
                list.Add(id);
            }
            int total = list.Count;
            if (total == 0) return 0;

            int next = -1, done = 0, loaded = 0;
            var tasks = new List<Task>();
            for (int t = 0; t < 4; t++)
            {
                tasks.Add(Task.Run(delegate
                {
                    while (true)
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= total) break;
                        if (GetOrFetch(list[i]) != null) Interlocked.Increment(ref loaded);
                        int d = Interlocked.Increment(ref done);
                        if (progress != null) { try { progress(d, total); } catch { } }
                    }
                }));
            }
            try { Task.WaitAll(tasks.ToArray()); } catch { }
            return loaded;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                foreach (KeyValuePair<int, Image> kv in _map)
                    if (kv.Value != null) kv.Value.Dispose();
                _map.Clear();
            }
            if (_cdb != null) { try { _cdb.Dispose(); } catch { } }
        }
    }
}
