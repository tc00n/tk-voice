using System.Windows;

namespace TKVoice.App;

public partial class ApiKeyWindow : Window
{
    public ApiKeyWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => KeyBox.Focus();
    }

    public string ApiKey => KeyBox.Password.Trim();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (ApiKey.Length == 0)
        {
            KeyBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
