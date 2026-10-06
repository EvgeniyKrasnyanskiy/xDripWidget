using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace XDripWidget
{
    public partial class MainWindow : Window
    {
        private readonly Config _config = new Config();
        private readonly ApiClient _apiClient = new ApiClient();
        private readonly DispatcherTimer _timer = new DispatcherTimer();
        private Forms.NotifyIcon _notifyIcon;
        private Forms.MenuItem _trayClickThroughItem;
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

            // Hook Win32 messages for real-time edge snapping and hotkeys
            var helper = new WindowInteropHelper(this);
            var source = HwndSource.FromHwnd(helper.Handle);
            if (source != null)
            {
                source.AddHook(WndProc);
            }

            // Register global hotkey
            UpdateGlobalHotkey();

            // Apply click-through mode if saved
            if (_config.ClickThrough)
            {
                SetClickThrough(true);
            }
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
            contextMenu.MenuItems.Add("Ввести терапию", (s, e) => Dispatcher.Invoke((Action)(() => MenuTreatments_Click(this, new RoutedEventArgs()))));
            _trayClickThroughItem = new Forms.MenuItem("Режим «Призрак» (сквозной клик)", (s, e) => ToggleClickThrough());
            _trayClickThroughItem.Checked = _config.ClickThrough;
            contextMenu.MenuItems.Add(_trayClickThroughItem);
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

            SnapToScreenEdges();

            // Save new position
            _config.SavePosition(Left, Top);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int WM_MOVING = 0x0216;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_TREATMENT_ID = 9001;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int SnapThresholdPx = 20;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOVING)
            {
                RECT rect = (RECT)Marshal.PtrToStructure(lParam, typeof(RECT));
                int cx = rect.Left + (rect.Right - rect.Left) / 2;
                int cy = rect.Top + (rect.Bottom - rect.Top) / 2;
                var screen = Forms.Screen.FromPoint(new System.Drawing.Point(cx, cy));
                var wa = screen.WorkingArea;

                int w = rect.Right - rect.Left;
                int h = rect.Bottom - rect.Top;

                // Horizontal snap
                if (Math.Abs(rect.Left - wa.Left) <= SnapThresholdPx)
                {
                    rect.Left = wa.Left;
                    rect.Right = wa.Left + w;
                }
                else if (Math.Abs(rect.Right - wa.Right) <= SnapThresholdPx)
                {
                    rect.Right = wa.Right;
                    rect.Left = wa.Right - w;
                }

                // Vertical snap
                if (Math.Abs(rect.Top - wa.Top) <= SnapThresholdPx)
                {
                    rect.Top = wa.Top;
                    rect.Bottom = wa.Top + h;
                }
                else if (Math.Abs(rect.Bottom - wa.Bottom) <= SnapThresholdPx)
                {
                    rect.Bottom = wa.Bottom;
                    rect.Top = wa.Bottom - h;
                }

                Marshal.StructureToPtr(rect, lParam, true);
                handled = true;
                return (IntPtr)1;
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_TREATMENT_ID)
            {
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    MenuTreatments_Click(this, new RoutedEventArgs());
                }));
                handled = true;
                return IntPtr.Zero;
            }
            return IntPtr.Zero;
        }

        private void ToggleClickThrough()
        {
            SetClickThrough(!_config.ClickThrough);
        }

        private void MenuClickThrough_Click(object sender, RoutedEventArgs e)
        {
            SetClickThrough(MenuClickThrough.IsChecked);
        }

        private void SetClickThrough(bool enable)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                    if (enable)
                    {
                        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                    }
                    else
                    {
                        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    }
                }
                _config.SaveClickThrough(enable);
                UpdateClickThroughUI(enable);

                if (enable && _notifyIcon != null)
                {
                    _notifyIcon.ShowBalloonTip(3500, "xDripWidget: Режим «Призрак»", "Сквозной клик активен. Отключить можно через контекстное меню иконки в трее.", Forms.ToolTipIcon.Info);
                }
            }
            catch { }
        }

        private void UpdateClickThroughUI(bool enabled)
        {
            if (MenuClickThrough != null)
            {
                MenuClickThrough.IsChecked = enabled;
            }
            if (_trayClickThroughItem != null)
            {
                _trayClickThroughItem.Checked = enabled;
            }
        }

        private void UpdateGlobalHotkey()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle == IntPtr.Zero) return;

                UnregisterHotKey(helper.Handle, HOTKEY_TREATMENT_ID);

                uint mods, vk;
                if (ParseHotkey(_config.TreatmentHotkey, out mods, out vk))
                {
                    RegisterHotKey(helper.Handle, HOTKEY_TREATMENT_ID, mods, vk);
                }
            }
            catch { }
        }

        private bool ParseHotkey(string hotkeyStr, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;
            if (string.IsNullOrWhiteSpace(hotkeyStr)) return false;

            string[] parts = hotkeyStr.Split('+');
            if (parts.Length == 0) return false;

            string keyPart = null;
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_CONTROL;
                else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_ALT;
                else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_SHIFT;
                else if (p.Equals("Win", StringComparison.OrdinalIgnoreCase) || p.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    modifiers |= MOD_WIN;
                else
                    keyPart = p;
            }

            if (string.IsNullOrEmpty(keyPart)) return false;

            try
            {
                Key wpfKey = (Key)Enum.Parse(typeof(Key), keyPart, true);
                vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
                modifiers |= MOD_NOREPEAT;
                return vk > 0;
            }
            catch
            {
                return false;
            }
        }

        private void SnapToScreenEdges()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
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
                        double workLeft = screen.WorkingArea.Left / dpiX;
                        double workTop = screen.WorkingArea.Top / dpiY;
                        double workRight = screen.WorkingArea.Right / dpiX;
                        double workBottom = screen.WorkingArea.Bottom / dpiY;

                        const double snapDips = 20.0;
                        double w = ActualWidth > 0 ? ActualWidth : Width;
                        double h = ActualHeight > 0 ? ActualHeight : Height;

                        if (Math.Abs(Left - workLeft) <= snapDips)
                        {
                            Left = workLeft;
                        }
                        else if (Math.Abs((Left + w) - workRight) <= snapDips)
                        {
                            Left = workRight - w;
                        }

                        if (Math.Abs(Top - workTop) <= snapDips)
                        {
                            Top = workTop;
                        }
                        else if (Math.Abs((Top + h) - workBottom) <= snapDips)
                        {
                            Top = workBottom - h;
                        }
                    }
                }
            }
            catch { }
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
                UpdateGlobalHotkey();
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

            try
            {
                var helper = new WindowInteropHelper(this);
                if (helper.Handle != IntPtr.Zero)
                {
                    UnregisterHotKey(helper.Handle, HOTKEY_TREATMENT_ID);
                }
            }
            catch { }

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
