using System.Windows;
using System.Windows.Controls;
using McServerLauncher.Core.Servers;

namespace McServerLauncher.App;

public partial class AddServerDialog : Window
{
    public string ServerName { get; private set; } = "";
    public ServerType SelectedType { get; private set; } = ServerType.Paper;
    public string SelectedLevelType { get; private set; } = "DEFAULT";

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
        SelectedType = Enum.Parse<ServerType>((string)((ComboBoxItem)ServerTypeCombo.SelectedItem).Tag);
        SelectedLevelType = (string)((ComboBoxItem)MapCombo.SelectedItem).Tag;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
