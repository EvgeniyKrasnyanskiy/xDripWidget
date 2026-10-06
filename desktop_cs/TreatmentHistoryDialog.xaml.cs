using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace XDripWidget
{
    public class TreatmentViewItem
    {
        public string Id { get; set; }
        public string EventType { get; set; }
        public string DateStr { get; set; }
        public string InsulinStr { get; set; }
        public string CarbsStr { get; set; }
        public string GlucoseStr { get; set; }
        public string Notes { get; set; }
    }

    public partial class TreatmentHistoryDialog : Window
    {
        private readonly ApiClient _apiClient;
        private readonly string _baseUrl;
        private readonly string _apiSecret;
        private readonly ObservableCollection<TreatmentViewItem> _items = new ObservableCollection<TreatmentViewItem>();

        public TreatmentHistoryDialog(ApiClient apiClient, string baseUrl, string apiSecret)
        {
            InitializeComponent();
            DarkThemeHelper.ApplyDarkTitleBar(this);
            _apiClient = apiClient;
            _baseUrl = baseUrl;
            _apiSecret = apiSecret;

            ListTreatments.ItemsSource = _items;
            LoadTreatmentsAsync();
        }

        private static string FormatEventType(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "—";
            switch (raw.Trim())
            {
                case "Correction Bolus": return "Коррекция";
                case "Meal Bolus": return "Болюс на еду";
                case "Carb Intake": return "Углеводы";
                case "Note": return "Заметка";
                case "BG Check": return "Замер СК";
                case "Snack Bolus": return "Перекус";
                case "Combo Bolus": return "Квадратный болюс";
                case "Temp Basal": return "ВБС (базал)";
                case "Profile Switch": return "Смена профиля";
                case "Site Change": return "Смена канюли";
                case "Sensor Change": return "Смена сенсора";
                case "Insulin Cartridge Change": return "Смена картриджа";
                case "Sensor Start": return "Старт сенсора";
                default: return raw;
            }
        }

        private async void LoadTreatmentsAsync()
        {
            _items.Clear();
            try
            {
                var list = await _apiClient.GetTreatmentsAsync(_baseUrl, _apiSecret, 50);
                foreach (var t in list)
                {
                    _items.Add(new TreatmentViewItem
                    {
                        Id = t.Id,
                        EventType = FormatEventType(t.EventType),
                        DateStr = t.Date.ToString("dd.MM.yyyy HH:mm"),
                        InsulinStr = t.Insulin > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:F1} ЕД", t.Insulin) : "—",
                        CarbsStr = t.Carbs > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:F0} г", t.Carbs) : "—",
                        GlucoseStr = t.Glucose > 0 ? string.Format(CultureInfo.InvariantCulture, "{0:F1}", t.Glucose) : "—",
                        Notes = t.Notes
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Не удалось загрузить историю: {0}", ex.Message), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadTreatmentsAsync();
        }

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var sel = ListTreatments.SelectedItem as TreatmentViewItem;
            if (sel == null)
            {
                MessageBox.Show("Выберите запись для удаления.", "xDripWidget", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(string.Format("Удалить запись от {0} ({1})?", sel.DateStr, sel.EventType), "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                bool deleted = await _apiClient.DeleteTreatmentAsync(_baseUrl, _apiSecret, sel.Id);
                if (deleted)
                {
                    _items.Remove(sel);
                }
                else
                {
                    MessageBox.Show("Не удалось удалить запись с сервера.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format("Ошибка удаления: {0}", ex.Message), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
