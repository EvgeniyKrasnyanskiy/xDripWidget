using System;
using System.IO;

namespace XDripWidget
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logPath;
        private const long MaxLogBytes = 1024 * 1024; // 1 MB

        static Logger()
        {
            try
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widget_cs.log");
            }
            catch
            {
                _logPath = "widget_cs.log";
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            string msg = ex != null ? string.Format("{0} | Ex: {1}", message, ex.Message) : message;
            Write("ERROR", msg);
        }

        private static void Write(string level, string message)
        {
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
