using System;
using System.IO;

namespace XDripWidget
{
    public enum LogLevel
    {
        Off = 0,
        Error = 1,
        Warn = 2,
        Info = 3,
        Debug = 4
    }

    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logPath;
        private const long MaxLogBytes = 1024 * 1024; // 1 MB

        public static LogLevel CurrentLevel { get; set; }

        static Logger()
        {
            CurrentLevel = LogLevel.Info;
            try
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widget_cs.log");
            }
            catch
            {
                _logPath = "widget_cs.log";
            }
        }

        public static void SetLevel(string levelStr)
        {
            if (string.IsNullOrWhiteSpace(levelStr))
            {
                CurrentLevel = LogLevel.Info;
                return;
            }

            string s = levelStr.Trim().ToLowerInvariant();
            if (s == "off" || s == "none" || s == "false" || s == "0")
            {
                CurrentLevel = LogLevel.Off;
            }
            else if (s == "error" || s == "err")
            {
                CurrentLevel = LogLevel.Error;
            }
            else if (s == "warn" || s == "warning")
            {
                CurrentLevel = LogLevel.Warn;
            }
            else if (s == "debug")
            {
                CurrentLevel = LogLevel.Debug;
            }
            else
            {
                CurrentLevel = LogLevel.Info;
            }
        }

        public static void Debug(string message)
        {
            if (CurrentLevel >= LogLevel.Debug)
            {
                Write("DEBUG", message);
            }
        }

        public static void Info(string message)
        {
            if (CurrentLevel >= LogLevel.Info)
            {
                Write("INFO", message);
            }
        }

        public static void Warn(string message)
        {
            if (CurrentLevel >= LogLevel.Warn)
            {
                Write("WARN", message);
            }
        }

        public static void Error(string message, Exception ex = null)
        {
            if (CurrentLevel >= LogLevel.Error)
            {
                string msg = ex != null ? string.Format("{0} | Ex: {1}", message, ex.Message) : message;
                Write("ERROR", msg);
            }
        }

        private static void Write(string level, string message)
        {
            if (CurrentLevel == LogLevel.Off) return;

            try
            {
                lock (_lock)
                {
                    var fi = new FileInfo(_logPath);
                    if (fi.Exists && fi.Length > MaxLogBytes)
                    {
                        string oldPath = _logPath + ".old";
                        try
                        {
                            if (File.Exists(oldPath)) File.Delete(oldPath);
                            File.Move(_logPath, oldPath);
                        }
                        catch { }
                    }

                    string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}{3}", DateTime.Now, level, message, Environment.NewLine);
                    File.AppendAllText(_logPath, line, System.Text.Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
