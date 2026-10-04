using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace XDripWidget
{
    public partial class TreatmentDialog : Window
    {
        private readonly ApiClient _apiClient;
        private readonly string _baseUrl;
        private readonly string _apiSecret;

        private readonly Dictionary<string, string> _eventMap = new Dictionary<string, string>
        {
            { "Приём пищи (Углеводы + Инсулин)", "Meal Bolus" },
            { "Коррекция инсулином",             "Correction Bolus" },
            { "Перекус / Углеводы",              "Carb Intake" },
            { "Замер сахара крови",              "BG Check" },
            { "Заметка",                         "Note" }
        };

        public TreatmentDialog(ApiClient apiClient, string baseUrl, string apiSecret)
        {
            InitializeComponent();
            _apiClient = apiClient;
            _baseUrl = baseUrl;
            _apiSecret = apiSecret;

            foreach (var item in _eventMap.Keys)
            {
                CmbEventType.Items.Add(item);
            }
            CmbEventType.SelectedIndex = 0;

            TxtDateTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        }

        private void CmbEventType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string sel = CmbEventType.SelectedItem as string;
            if (string.IsNullOrEmpty(sel)) return;

            string ev;
            if (!_eventMap.TryGetValue(sel, out ev)) ev = "Meal Bolus";

            // Dim/focus appropriate fields
            TxtInsulin.IsEnabled = (ev == "Meal Bolus" || ev == "Correction Bolus");
            TxtCarbs.IsEnabled = (ev == "Meal Bolus" || ev == "Carb Intake");
            TxtGlucose.IsEnabled = (ev == "BG Check");
            TxtNotes.IsEnabled = true;

            TxtInsulin.Opacity = TxtInsulin.IsEnabled ? 1.0 : 0.4;
            TxtCarbs.Opacity = TxtCarbs.IsEnabled ? 1.0 : 0.4;
            TxtGlucose.Opacity = TxtGlucose.IsEnabled ? 1.0 : 0.4;
        }

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            string sel = CmbEventType.SelectedItem as string;
            string eventType = "Meal Bolus";
            if (!string.IsNullOrEmpty(sel)) _eventMap.TryGetValue(sel, out eventType);

            double insulin = ParseDouble(TxtInsulin.Text);
            double carbs = ParseDouble(TxtCarbs.Text);
            double glucose = ParseDouble(TxtGlucose.Text);
            string notes = TxtNotes.Text.Trim();

            DateTime dt;
            if (!DateTime.TryParse(TxtDateTime.Text, out dt))
            {
                dt = DateTime.Now;
            }

            IsEnabled = false;
            try
            {
                bool success = await _apiClient.SubmitTreatmentAsync(_baseUrl, _apiSecret, eventType, carbs, insulin, glucose, notes, dt);
                if (success)
                {
                    MessageBox.Show("Терапия успешно отправлена на сервер!", "Успешно", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Не удалось отправить терапию на сервер. Проверьте адрес и токен.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Ошибка отправки: {0}", ex.Message), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static double ParseDouble(string str)
        {
            if (string.IsNullOrWhiteSpace(str)) return 0.0;
            double res;
            string s = str.Trim().Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out res))
            {
                return res;
            }
            return 0.0;
        }
    }
}
