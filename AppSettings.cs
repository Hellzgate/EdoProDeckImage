using System;
using System.IO;
using System.Text.Json;

namespace EdoProDeckImage
{
    /// <summary>%LOCALAPPDATA%\EdoProDeckImage\settings.json 에 저장되는 사용자 설정(쓰기 가능하면 &lt;exe&gt;\data\).</summary>
    public sealed class AppSettings
    {
        public string LastDeckPath { get; set; } = "";
        public string PicsFolder { get; set; } = "";
        public string OutputFolder { get; set; } = "";
        public string Columns { get; set; } = "10";
        public string CardWidth { get; set; } = "110";
        public string Gap { get; set; } = "5";
        public string Theme { get; set; } = "dark";
        public string Format { get; set; } = "PNG";
        public bool ShowCounts { get; set; } = false;
        public bool ShowTitle { get; set; } = true;
        public bool ShowFooter { get; set; } = true;
        public bool ShowSide { get; set; } = true;
        public bool NetImages { get; set; } = true;
        public string LastTitle { get; set; } = "";
        /// <summary>창 크기 "1240x890" (2026-11-08).</summary>
        public string WindowSize { get; set; } = "";

        // ── 저장 위치 ────────────────────────────────────────────
        //  1순위: exe 옆의 data\  (포터블: 폴더째 복사/이동해도 설정·캐시·로그가 따라온다)
        //  쓰기 불가(예: Program Files)면 %LOCALAPPDATA%\EdoProDeckImage 로 폴백.
        private static string _folder;

        public static string Folder
        {
            get
            {
                if (_folder != null) return _folder;

                string preferred = null;
                try { preferred = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"); } catch { }

                if (preferred != null && CanWrite(preferred))
                {
                    _folder = preferred;
                }
                else
                {
                    string b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (string.IsNullOrEmpty(b)) b = Path.GetTempPath();
                    _folder = Path.Combine(b, "EdoProDeckImage");
                }

                try { Directory.CreateDirectory(_folder); } catch { }
                MigrateOldFolder(_folder);
                return _folder;
            }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".wtest");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>예전 위치(%LOCALAPPDATA%)의 설정/캐시를 새 폴더로 1회 옮긴다.</summary>
        private static void MigrateOldFolder(string newFolder)
        {
            try
            {
                string b = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(b)) return;
                // 2026-11-08: 앱 이름 변경(WindBotDeckImage → EdoProDeckImage) — 예전 폴더에서 1회 옮긴다
                string old = Path.Combine(b, "WindBotDeckImage");
                if (string.Equals(Path.GetFullPath(old), Path.GetFullPath(newFolder), StringComparison.OrdinalIgnoreCase)) return;

                string oldSettings = Path.Combine(old, "settings.json");
                string newSettings = Path.Combine(newFolder, "settings.json");
                if (File.Exists(oldSettings) && !File.Exists(newSettings))
                    File.Copy(oldSettings, newSettings, true);

                string oldCache = Path.Combine(old, "imgcache");
                string newCache = Path.Combine(newFolder, "imgcache");
                if (Directory.Exists(oldCache) && !Directory.Exists(newCache))
                {
                    try { Directory.Move(oldCache, newCache); }
                    catch { /* 실패하면 그냥 새로 받는다 */ }
                }
            }
            catch { }
        }

        public static string FilePath { get { return Path.Combine(Folder, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    AppSettings s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
