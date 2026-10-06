using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace XDripWidget
{
    public class Config
    {
        private readonly string _configPath;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filePath);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retVal, int size, string filePath);

        public string ServerUrl { get; set; }
        public string ApiSecret { get; set; }
        public int Transparency { get; set; }
        public int RefreshIntervalMinutes { get; set; }
        public double WindowX { get; set; }
        public double WindowY { get; set; }
        public string TreatmentHotkey { get; set; }
        public bool ClickThrough { get; set; }
        public bool AcrylicBlur { get; set; }
        public bool CompactMode { get; set; }

        public Config()
        {
            ServerUrl = "http://localhost:8080";
            ApiSecret = "";
            Transparency = 10;
            RefreshIntervalMinutes = 1;
            WindowX = -1;
            WindowY = -1;
            TreatmentHotkey = "Ctrl+Alt+D";
            ClickThrough = false;
            AcrylicBlur = true;
            CompactMode = false;

            _configPath = GetConfigFilePath();
            Load();
        }

        private string GetConfigFilePath()
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            if (File.Exists(localPath))
            {
                return localPath;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "xDripWidget");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return Path.Combine(dir, "config.ini");
        }

        public void Load()
        {
            if (!File.Exists(_configPath))
            {
                Save();
                return;
            }

            ServerUrl = ReadKey("General", "server_url", "http://localhost:8080");
            ApiSecret = ReadKey("General", "api_secret", "");

            string tStr = ReadKey("General", "transparency", "");
            int t;
            if (int.TryParse(tStr, out t))
            {
                Transparency = Math.Max(0, Math.Min(70, t));
            }
            else
            {
                string oStr = ReadKey("General", "opacity", "90");
                int o;
                if (int.TryParse(oStr, out o))
                {
                    Transparency = Math.Max(0, Math.Min(70, 100 - o));
                }
            }

            string intStr = ReadKey("General", "refresh_interval", "1");
            int interval;
            if (int.TryParse(intStr, out interval))
            {
                RefreshIntervalMinutes = Math.Max(1, Math.Min(60, interval));
            }

            string xStr = ReadKey("Position", "x", "-1");
            double x;
            if (double.TryParse(xStr, out x)) WindowX = x;

            string yStr = ReadKey("Position", "y", "-1");
            double y;
            if (double.TryParse(yStr, out y)) WindowY = y;

            TreatmentHotkey = ReadKey("General", "hotkey_treatment", "Ctrl+Alt+D");
            bool ct;
            if (bool.TryParse(ReadKey("General", "click_through", "false"), out ct))
            {
                ClickThrough = ct;
            }

            bool ab;
            if (bool.TryParse(ReadKey("General", "acrylic_blur", "true"), out ab))
            {
                AcrylicBlur = ab;
            }

            bool cm;
            if (bool.TryParse(ReadKey("General", "compact_mode", "false"), out cm))
            {
                CompactMode = cm;
            }
        }

        public void Save()
        {
            try
            {
                WriteKey("General", "server_url", ServerUrl);
                WriteKey("General", "api_secret", ApiSecret);
                WriteKey("General", "transparency", Transparency.ToString());
                WriteKey("General", "refresh_interval", RefreshIntervalMinutes.ToString());
                WriteKey("General", "hotkey_treatment", TreatmentHotkey ?? "");
                WriteKey("General", "click_through", ClickThrough ? "true" : "false");
                WriteKey("General", "acrylic_blur", AcrylicBlur ? "true" : "false");
                WriteKey("General", "compact_mode", CompactMode ? "true" : "false");
                if (WindowX >= 0 && WindowY >= 0)
                {
                    WriteKey("Position", "x", WindowX.ToString("F0"));
                    WriteKey("Position", "y", WindowY.ToString("F0"));
                }
            }
            catch { }
        }

        public void SavePosition(double x, double y)
        {
            WindowX = x;
            WindowY = y;
            WriteKey("Position", "x", x.ToString("F0"));
            WriteKey("Position", "y", y.ToString("F0"));
        }

        public void SaveClickThrough(bool enabled)
        {
            ClickThrough = enabled;
            WriteKey("General", "click_through", enabled ? "true" : "false");
        }

        public void SaveCompactMode(bool enabled)
        {
            CompactMode = enabled;
            WriteKey("General", "compact_mode", enabled ? "true" : "false");
        }

        public void SaveAcrylicBlur(bool enabled)
        {
            AcrylicBlur = enabled;
            WriteKey("General", "acrylic_blur", enabled ? "true" : "false");
        }

        private string ReadKey(string section, string key, string defaultValue)
        {
            var sb = new StringBuilder(512);
            GetPrivateProfileString(section, key, defaultValue, sb, 512, _configPath);
            return sb.ToString();
        }

        private void WriteKey(string section, string key, string value)
        {
            WritePrivateProfileString(section, key, value, _configPath);
        }

        public static bool IsRunOnStartupEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("xDripWidget") as string;
                        return !string.IsNullOrEmpty(val);
                    }
                }
            }
            catch { }
            return false;
        }

        public static void SetRunOnStartup(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            key.SetValue("xDripWidget", "\"" + exePath + "\"");
                        }
                        else
                        {
                            key.DeleteValue("xDripWidget", false);
                        }
                    }
                }
            }
            catch { }
        }
    }
}
