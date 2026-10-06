using System.Windows;

namespace XDripWidget
{
    public partial class ConfirmExitDialog : Window
    {
        public ConfirmExitDialog()
        {
            InitializeComponent();
            DarkThemeHelper.ApplyDarkTitleBar(this);
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
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
