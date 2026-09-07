using System.Windows;

namespace McServerLauncher.App;

public partial class AddServerDialog : Window
{
    public string ServerName { get; private set; } = "";

    public AddServerDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("サーバー名を入力してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ServerName = name;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
