using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace WindBotDeckImage
{
    /// <summary>
    /// 카드 이미지 캐시. 렌더 사이에 재사용하고, 없으면 (옵션) 인터넷에서 받아온다.
    ///   · 로컬: &lt;pics&gt;\&lt;카드ID&gt;.jpg / .png
    ///   · 인터넷: https://images.ygoprodeck.com/images/cards/&lt;카드ID&gt;.jpg → 캐시 폴더에 저장
    /// </summary>
    public sealed class CardImageStore : IDisposable
    {
        private const string UrlTemplate = "https://images.ygoprodeck.com/images/cards/{0}.jpg";

        private readonly string _picsFolder;
        private readonly bool _allowNetwork;
        private readonly string _cacheDir;

        private readonly Dictionary<int, Image> _map = new Dictionary<int, Image>();
        private readonly HashSet<int> _tried = new HashSet<int>();
        private readonly object _gate = new object();

        private int _fromNetwork;
        private int _fromCache;
        private int _attempts;

        public CardImageStore(string picsFolder, bool allowNetwork, string cacheDir)
        {
            _picsFolder = picsFolder ?? "";
            _allowNetwork = allowNetwork;
            _cacheDir = cacheDir ?? "";
            if (_allowNetwork && _cacheDir.Length > 0)
            {
                try { Directory.CreateDirectory(_cacheDir); } catch { }
            }
        }

        public int FromNetwork { get { return _fromNetwork; } }
        public int Attempts { get { return _attempts; } }
        /// <summary>캐시 파일에서 읽은 장수(이미 받아 둔 것).</summary>
        public int FromCache { get { return _fromCache; } }
        public bool HasNetwork { get { return _allowNetwork; } }
        public int Count { get { lock (_gate) return _map.Count; } }

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
            if (_picsFolder.Length == 0 || !Directory.Exists(_picsFolder)) return null;
            foreach (string ext in new[] { ".jpg", ".png" })
            {
                string p = Path.Combine(_picsFolder, cardId.ToString() + ext);
                if (!File.Exists(p)) continue;
                try { return Image.FromFile(p); } catch { }
            }
            return null;
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
                string url = string.Format(UrlTemplate, cardId);
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;
                req.UserAgent = "WindBotDeckImage/1.1 (Windows)";

                byte[] bytes;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    bytes = ms.ToArray();
                }
                if (bytes == null || bytes.Length <= 512) return null;

                try { File.WriteAllBytes(cached, bytes); } catch { }
                downloaded = true;

                using (var ms = new MemoryStream(bytes))
                {
                    var img = Image.FromStream(ms);
                    return (Image)img.Clone();   // 스트림 의존 제거
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>여러 장 미리 읽기(4 병렬). progress(완료, 전체)</summary>
        public int Preload(IEnumerable<int> ids, Action<int, int> progress)
        {
            if (ids == null) return 0;
            var list = new List<int>();
            foreach (int id in ids)
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
        }
    }
}
