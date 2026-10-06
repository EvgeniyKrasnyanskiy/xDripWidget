using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace XDripWidget
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        public static readonly uint WM_SHOWWIDGET = RegisterWindowMessage("XDripWidget_ShowWindow_Msg");

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "widget_cs.log");
                    string logEntry = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] Unhandled: {1}\n", DateTime.Now, args.Exception);
                    File.AppendAllText(logPath, logEntry);
                }
                catch { }
                args.Handled = true;
            };

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\XDripWidget_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                // Silently wake up existing instance and bring to front without annoying popups
                if (WM_SHOWWIDGET != 0)
                {
                    PostMessage(HWND_BROADCAST, WM_SHOWWIDGET, IntPtr.Zero, IntPtr.Zero);
                }
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                    _singleInstanceMutex.Dispose();
                }
                catch { }
            }
        }
    }
}
