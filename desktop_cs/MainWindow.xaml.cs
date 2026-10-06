using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace XDripWidget
{
    public partial class MainWindow : Window
    {
        private readonly Config _config = new Config();
        private readonly ApiClient _apiClient = new ApiClient();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private Forms.NotifyIcon _notifyIcon;
        private System.Windows.Point? _dragStartScreenPos;
        private bool _isFetching = false;

        private DateTime _lastHypoAlert = DateTime.MinValue;
        private DateTime _lastHyperAlert = DateTime.MinValue;
        private DateTime _lastCriticalAlert = DateTime.MinValue;
        private static readonly TimeSpan AlertCooldown = TimeSpan.FromHours(1);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Position
            if (_config.WindowX >= 0 && _config.WindowY >= 0)
            {
                Left = _config.WindowX;
                Top = _config.WindowY;
            }
            else
            {
                Left = SystemParameters.WorkArea.Width - Width - 30;
                Top = 60;
            }

            // Opacity
            Opacity = Math.Max(0.3, Math.Min(1.0, (100 - _config.Transparency) / 100.0));

            // Setup Tray Icon
            SetupTrayIcon();

            // Setup Timer
            int interval = Math.Max(1, _config.RefreshIntervalMinutes);
            _timer.Interval = TimeSpan.FromMinutes(interval);
            _timer.Tick += (s, args) => FetchDataAsync();
            _timer.Start();

            // Initial fetch
            FetchDataAsync();
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Text = "xDrip Widget",
                Visible = true,
                Icon = CreateBloodDropIcon(System.Drawing.Color.FromArgb(148, 163, 184))
            };

            var contextMenu = new Forms.ContextMenu();
            contextMenu.MenuItems.Add("Показать / Скрыть", (s, e) => ToggleVisibility());
            contextMenu.MenuItems.Add("Обновить сейчас", (s, e) => FetchDataAsync());
            contextMenu.MenuItems.Add("Выход", (s, e) => Dispatcher.Invoke((Action)ConfirmAndQuit));
            _notifyIcon.ContextMenu = contextMenu;

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == Forms.MouseButtons.Left)
                {
                    ToggleVisibility();
                }
            };
        }

        private void ToggleVisibility()
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
            }
            else
            {
                Show();
                Activate();
            }
        }

        private async void FetchDataAsync()
        {
            if (_isFetching) return;
            _isFetching = true;

            try
            {
                var result = await _apiClient.FetchAllAsync(_config.ServerUrl, _config.ApiSecret);
                if (result.IsSuccess)
                {
                    CanvasElement.UpdateData(result.CurrentData, result.History);
                    UpdateTray(result.CurrentData);
                    CheckAlerts(result.CurrentData);
                }
                else
                {
                    CanvasElement.SetError(result.ErrorMessage);
                    _notifyIcon.Text = string.Format("xDrip Widget: {0}", result.ErrorMessage);
                    if (_notifyIcon.Text.Length >= 64) _notifyIcon.Text = _notifyIcon.Text.Substring(0, 63);
                    _notifyIcon.Icon = CreateBloodDropIcon(System.Drawing.Color.FromArgb(148, 163, 184));
                }
            }
            catch (Exception ex)
            {
                CanvasElement.SetError(ex.Message);
            }
            finally
            {
                _isFetching = false;
            }
        }

        private void UpdateTray(CurrentGlucoseData data)
        {
            if (data == null || _notifyIcon == null) return;

            var wpfColor = Constants.GetGlucoseColor(data.Mmol, data.IsStale);
            var gdiColor = System.Drawing.Color.FromArgb(wpfColor.R, wpfColor.G, wpfColor.B);
            _notifyIcon.Icon = CreateBloodDropIcon(gdiColor);

            string arrow = Constants.GetTrendArrow(data.Direction);
            string timeStr = Constants.FormatTimeAgo(data.MinutesAgo);
            string tooltip = string.Format("xDrip Widget: {0:F1} {1} ({2})", data.Mmol, arrow, timeStr);
            if (tooltip.Length >= 64) tooltip = tooltip.Substring(0, 63);
            _notifyIcon.Text = tooltip;
        }

        private void CheckAlerts(CurrentGlucoseData data)
        {
            if (data == null || data.IsStale || data.Mmol <= 0) return;

            var now = DateTime.Now;
            if (data.Mmol >= Constants.ALERT_CRITICAL && (now - _lastCriticalAlert) > AlertCooldown)
            {
                _lastCriticalAlert = now;
                _notifyIcon.ShowBalloonTip(10000, "⛔ Критический сахар!", string.Format("{0:F1} ммоль/л — немедленно примите меры!", data.Mmol), Forms.ToolTipIcon.Error);
            }
            else if (data.Mmol >= Constants.ALERT_HYPER && (now - _lastHyperAlert) > AlertCooldown)
            {
                _lastHyperAlert = now;
                _notifyIcon.ShowBalloonTip(7000, "🟡 Высокий сахар", string.Format("{0:F1} ммоль/л — выше нормы.", data.Mmol), Forms.ToolTipIcon.Warning);
            }
            else if (data.Mmol <= Constants.ALERT_HYPO && (now - _lastHypoAlert) > AlertCooldown)
            {
                _lastHypoAlert = now;
                _notifyIcon.ShowBalloonTip(10000, "🔴 Низкий сахар!", string.Format("{0:F1} ммоль/л — опасная гипогликемия!", data.Mmol), Forms.ToolTipIcon.Error);
            }
        }

        private System.Drawing.Icon CreateBloodDropIcon(System.Drawing.Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                using (var path = new GraphicsPath())
                {
                    float cx = 16f;
                    float cy = 19f;
                    float r = 10f;

                    path.AddBezier(cx, 2f, cx + r * 1.25f, cy - r * 0.2f, cx + r, cy + r, cx, cy + r);
                    path.AddBezier(cx, cy + r, cx - r, cy + r, cx - r * 1.25f, cy - r * 0.2f, cx, 2f);

                    using (var brush = new SolidBrush(color))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(200, color), 1f))
                    {
                        g.FillPath(brush, path);
                        g.DrawPath(pen, path);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                return System.Drawing.Icon.FromHandle(hIcon);
            }
        }

        // Dragging & Click handlers
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartScreenPos = PointToScreen(e.GetPosition(this));
            DragMove();
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragStartScreenPos.HasValue)
            {
                var endPos = PointToScreen(e.GetPosition(this));
                double distance = (endPos - _dragStartScreenPos.Value).Length;
                if (distance < 5)
                {
                    // Instant tap/click refresh
                    FetchDataAsync();
                }
            }
            _dragStartScreenPos = null;

            // Save new position
            _config.SavePosition(Left, Top);
        }

        private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ContextMenu != null)
            {
                ContextMenu.PlacementTarget = this;
                ContextMenu.IsOpen = true;
            }
        }

        private void MenuRefresh_Click(object sender, RoutedEventArgs e)
        {
            FetchDataAsync();
        }

        private void MenuHide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void PositionDialogNearWidget(Window dlg)
        {
            dlg.WindowStartupLocation = WindowStartupLocation.Manual;

            double workLeft = SystemParameters.WorkArea.Left;
            double workTop = SystemParameters.WorkArea.Top;
            double workRight = SystemParameters.WorkArea.Right;
            double workBottom = SystemParameters.WorkArea.Bottom;

            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    var screen = Forms.Screen.FromHandle(helper.Handle);
                    var source = PresentationSource.FromVisual(this);
                    double dpiX = 1.0;
                    double dpiY = 1.0;
                    if (source != null && source.CompositionTarget != null)
                    {
                        dpiX = source.CompositionTarget.TransformToDevice.M11;
                        dpiY = source.CompositionTarget.TransformToDevice.M22;
                    }

                    if (dpiX > 0 && dpiY > 0)
                    {
                        workLeft = screen.WorkingArea.Left / dpiX;
                        workTop = screen.WorkingArea.Top / dpiY;
                        workRight = screen.WorkingArea.Right / dpiX;
                        workBottom = screen.WorkingArea.Bottom / dpiY;
                    }
                }
            }
            catch
            {
                // Fallback to SystemParameters.WorkArea
            }

            double dlgWidth = dlg.Width > 0 ? dlg.Width : 400;
            double dlgHeight = dlg.Height > 0 ? dlg.Height : 300;
            if (double.IsNaN(dlg.Height) || dlg.Height <= 0)
            {
                dlg.Measure(new System.Windows.Size(dlgWidth, double.PositiveInfinity));
                if (dlg.DesiredSize.Height > 0)
                {
                    dlgHeight = dlg.DesiredSize.Height;
                }
            }

            const double margin = 10;
            double widgetWidth = ActualWidth > 0 ? ActualWidth : Width;
            double targetLeft;
            double targetTop = Top;

            // Prefer right, fallback to left
            if (Left + widgetWidth + margin + dlgWidth <= workRight)
            {
                targetLeft = Left + widgetWidth + margin;
            }
            else if (Left - margin - dlgWidth >= workLeft)
            {
                targetLeft = Left - margin - dlgWidth;
            }
            else
            {
                // Clamp within screen boundaries
                targetLeft = Math.Max(workLeft + margin, workRight - dlgWidth - margin);
            }

            // Align vertically with widget and clamp within working area
            if (targetTop + dlgHeight > workBottom)
            {
                targetTop = Math.Max(workTop + margin, workBottom - dlgHeight - margin);
            }
            if (targetTop < workTop)
            {
                targetTop = workTop + margin;
            }

            dlg.Left = targetLeft;
            dlg.Top = targetTop;

            dlg.Loaded += (s, args) =>
            {
                if (dlg.ActualHeight > 0 && dlg.Top + dlg.ActualHeight > workBottom)
                {
                    dlg.Top = Math.Max(workTop + margin, workBottom - dlg.ActualHeight - margin);
                }
            };
        }

        private void MenuTreatments_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TreatmentDialog(_apiClient, _config.ServerUrl, _config.ApiSecret);
            dlg.Owner = this;
            PositionDialogNearWidget(dlg);
            if (dlg.ShowDialog() == true)
            {
                FetchDataAsync();
            }
        }

        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new TreatmentHistoryDialog(_apiClient, _config.ServerUrl, _config.ApiSecret);
            dlg.Owner = this;
            PositionDialogNearWidget(dlg);
            dlg.ShowDialog();
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsDialog(_config);
            dlg.Owner = this;
            PositionDialogNearWidget(dlg);
            if (dlg.ShowDialog() == true)
            {
                Opacity = Math.Max(0.3, Math.Min(1.0, (100 - _config.Transparency) / 100.0));
                int interval = Math.Max(1, _config.RefreshIntervalMinutes);
                _timer.Interval = TimeSpan.FromMinutes(interval);
                FetchDataAsync();
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new AboutDialog();
            dlg.Owner = this;
            PositionDialogNearWidget(dlg);
            dlg.ShowDialog();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            ConfirmAndQuit();
        }

        private bool _isConfirmedQuit = false;

        private void ConfirmAndQuit()
        {
            Window owner = (this.IsVisible && this.WindowState != WindowState.Minimized) ? this : null;
            var res = owner != null
                ? MessageBox.Show(owner, "Вы действительно хотите выйти из xDrip Widget?", "Выход из программы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                : MessageBox.Show("Вы действительно хотите выйти из xDrip Widget?", "Выход из программы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

            if (res == MessageBoxResult.Yes)
            {
                _isConfirmedQuit = true;
                Close();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isConfirmedQuit)
            {
                Window owner = (this.IsVisible && this.WindowState != WindowState.Minimized) ? this : null;
                var res = owner != null
                    ? MessageBox.Show(owner, "Вы действительно хотите выйти из xDrip Widget?", "Выход из программы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
                    : MessageBox.Show("Вы действительно хотите выйти из xDrip Widget?", "Выход из программы", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);

                if (res != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                _isConfirmedQuit = true;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            _timer.Stop();
        }
    }
}
