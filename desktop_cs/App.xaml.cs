using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace XDripWidget
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Logger.Error("Критическое исключение AppDomain", args.ExceptionObject as Exception);
            };

            DispatcherUnhandledException += (s, args) =>
            {
                Logger.Error("Необработанное исключение (Unhandled Dispatcher Exception)", args.Exception);
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Logger.Error("Необработанное исключение TaskScheduler", args.Exception);
                args.SetObserved();
            };

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "Local\\XDripWidget_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                Logger.Info("Обнаружен уже запущенный экземпляр xDripWidget-CS. Активация существующего окна и выход дубликата.");
                IntPtr hwnd = FindWindow(null, "xDripWidget");
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow(hwnd);
                }
                Shutdown();
                return;
            }

            Logger.Info("=================== xDripWidget-CS запущен ===================");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            Logger.Info("=================== xDripWidget-CS завершил работу ===================");
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
