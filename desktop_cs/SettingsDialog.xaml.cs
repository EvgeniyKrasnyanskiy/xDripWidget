using System;
using System.Windows;

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
        }

        private void SliderTransparency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LblTransparency != null)
            {
                LblTransparency.Text = string.Format("{0:F0}%", e.NewValue);
            }
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
