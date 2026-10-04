using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace XDripWidget
{
    public partial class TreatmentDialog : Window
    {
        private readonly ApiClient _apiClient;
        private readonly string _baseUrl;
        private readonly string _apiSecret;

        public TreatmentDialog(ApiClient apiClient, string baseUrl, string apiSecret)
        {
            InitializeComponent();
            _apiClient = apiClient;
            _baseUrl = baseUrl;
            _apiSecret = apiSecret;

            TxtDateTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            Loaded += (s, e) =>
            {
                TxtInsulin.Focus();
            };

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && !TxtNotes.IsFocused)
                {
                    BtnSubmit_Click(this, new RoutedEventArgs());
                }
                else if (e.Key == Key.Escape)
                {
                    Close();
                }
            };
        }

        private async void BtnSubmit_Click(object sender, RoutedEventArgs e)
        {
            double insulin = ParseDouble(TxtInsulin.Text);
            double carbs = ParseDouble(TxtCarbs.Text);
            string notes = TxtNotes.Text.Trim();

            if (insulin <= 0 && carbs <= 0 && string.IsNullOrEmpty(notes))
            {
                MessageBox.Show("Введите количество инсулина, углеводов или заметку.", "xDripWidget", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Automatic event type determination
            string eventType;
            if (carbs > 0 && insulin > 0)
            {
                eventType = "Meal Bolus";
            }
            else if (carbs > 0)
            {
                eventType = "Carb Intake";
            }
            else if (insulin > 0)
            {
                eventType = "Correction Bolus";
            }
            else
            {
                eventType = "Note";
            }

            DateTime dt;
            if (!DateTime.TryParse(TxtDateTime.Text, out dt))
            {
                dt = DateTime.Now;
            }

            IsEnabled = false;
            try
            {
                bool success = await _apiClient.SubmitTreatmentAsync(_baseUrl, _apiSecret, eventType, carbs, insulin, 0.0, notes, dt);
                if (success)
                {
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
