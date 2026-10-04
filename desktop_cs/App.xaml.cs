using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace XDripWidget
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

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
                MessageBox.Show("Экземпляр xDripWidget уже запущен.", "xDripWidget", MessageBoxButton.OK, MessageBoxImage.Information);
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
