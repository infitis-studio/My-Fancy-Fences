using System.Windows;
using System.Windows.Input;

namespace My_Fancy_Fences;

public partial class RenameWindow : Window
{
    public string NewName { get; private set; } = string.Empty;

    public RenameWindow(string title, string currentName)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        Title = title;
        DialogTitleText.Text = title;
        CancelButton.Content = LocalizationService.T("Anuluj");
        SaveButton.Content = LocalizationService.T("Zapisz");
        NameTextBox.Text = currentName;
        NameTextBox.TextChanged += (_, _) => SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);
        Loaded += (_, _) =>
        {
            Topmost = true;
            Topmost = false;
            Activate();
            NameTextBox.Focus();
            NameTextBox.SelectAll();
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Save();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Save();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Save()
    {
        var name = NameTextBox.Text.Trim();
        if (name.Length == 0)
            return;

        NewName = name;
        DialogResult = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
