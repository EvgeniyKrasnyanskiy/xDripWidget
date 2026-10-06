using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace XDripWidget
{
    public partial class SettingsDialog : Window
    {
        private readonly Config _config;

        public SettingsDialog(Config config)
        {
            InitializeComponent();
            _config = config;

            TxtServerUrl.Text = _config.ServerUrl ?? "http://localhost:8080";
            TxtApiSecret.Password = _config.ApiSecret ?? "";
            SliderTransparency.Value = _config.Transparency;
            LblTransparency.Text = string.Format("{0}%", _config.Transparency);
            TxtInterval.Text = _config.RefreshIntervalMinutes.ToString();
            TxtHotkey.Text = string.IsNullOrEmpty(_config.TreatmentHotkey) ? "" : _config.TreatmentHotkey;
            ChkStartup.IsChecked = Config.IsRunOnStartupEnabled();
            ChkAcrylic.IsChecked = _config.AcrylicBlur;

            TxtLow.Text = _config.ThresholdLow.ToString("0.0", CultureInfo.InvariantCulture);
            TxtHigh.Text = _config.ThresholdHigh.ToString("0.0", CultureInfo.InvariantCulture);
            TxtUrgentLow.Text = _config.ThresholdUrgentLow.ToString("0.0", CultureInfo.InvariantCulture);
            TxtUrgentHigh.Text = _config.ThresholdUrgentHigh.ToString("0.0", CultureInfo.InvariantCulture);
            ChkSound.IsChecked = _config.SoundAlertsEnabled;
            ChkVisual.IsChecked = _config.VisualAlertsEnabled;
        }

        private void TxtHotkey_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            // Ignore standalone modifier keys
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            if (key == Key.Back || key == Key.Delete || key == Key.Escape)
            {
                TxtHotkey.Text = "";
                return;
            }

            var parts = new List<string>();
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
            if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
            if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");

            if (parts.Count == 0)
            {
                parts.Add("Ctrl");
                parts.Add("Alt");
            }

            parts.Add(key.ToString());
            TxtHotkey.Text = string.Join("+", parts.ToArray());
        }

        private void BtnClearHotkey_Click(object sender, RoutedEventArgs e)
        {
            TxtHotkey.Text = "";
        }

        private void SliderTransparency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblTransparency != null)
            {
                LblTransparency.Text = string.Format("{0:F0}%", e.NewValue);
            }
        }

        private void BtnTestSound_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Media.SystemSounds.Exclamation.Play();
            }
            catch { }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            _config.ServerUrl = TxtServerUrl.Text.Trim();
            _config.ApiSecret = TxtApiSecret.Password.Trim();
            _config.Transparency = (int)SliderTransparency.Value;

            int interval;
            if (int.TryParse(TxtInterval.Text.Trim(), out interval))
            {
                _config.RefreshIntervalMinutes = Math.Max(1, Math.Min(60, interval));
            }

            _config.TreatmentHotkey = TxtHotkey.Text.Trim();
            _config.AcrylicBlur = ChkAcrylic.IsChecked == true;
            Config.SetRunOnStartup(ChkStartup.IsChecked == true);

            double tLow;
            if (double.TryParse(TxtLow.Text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out tLow))
            {
                _config.ThresholdLow = Math.Max(2.0, Math.Min(10.0, tLow));
            }

            double tHigh;
            if (double.TryParse(TxtHigh.Text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out tHigh))
            {
                _config.ThresholdHigh = Math.Max(6.0, Math.Min(25.0, tHigh));
            }

            double tUrgentLow;
            if (double.TryParse(TxtUrgentLow.Text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out tUrgentLow))
            {
                _config.ThresholdUrgentLow = Math.Max(1.5, Math.Min(6.0, tUrgentLow));
            }

            double tUrgentHigh;
            if (double.TryParse(TxtUrgentHigh.Text.Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out tUrgentHigh))
            {
                _config.ThresholdUrgentHigh = Math.Max(10.0, Math.Min(30.0, tUrgentHigh));
            }

            _config.SoundAlertsEnabled = ChkSound.IsChecked == true;
            _config.VisualAlertsEnabled = ChkVisual.IsChecked == true;

            _config.Save();
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
