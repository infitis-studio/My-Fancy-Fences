using System.Windows;
using System.Windows.Input;

namespace My_Fancy_Fences;

public partial class LayoutRenameWindow : Window
{
    public LayoutRenameWindow(string currentName)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        LayoutNameTextBox.Text = currentName;
        Loaded += (_, _) =>
        {
            Activate();
            LayoutNameTextBox.Focus();
            LayoutNameTextBox.SelectAll();
        };
    }

    public string LayoutName => LayoutNameTextBox.Text.Trim();

    private void SaveButton_Click(object sender, RoutedEventArgs e) => Accept();

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LayoutNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Accept();
        e.Handled = true;
    }

    private void Accept()
    {
        if (string.IsNullOrWhiteSpace(LayoutName))
            return;

        DialogResult = true;
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
