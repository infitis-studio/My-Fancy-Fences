using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace My_Fancy_Fences;

public partial class LayoutManageWindow : Window
{
    private readonly ObservableCollection<EditableLayoutItem> _layouts;

    public LayoutManageWindow(IReadOnlyList<LayoutOverviewItem> layouts)
    {
        InitializeComponent();
        Icon = AppIconProvider.Image;
        _layouts = new ObservableCollection<EditableLayoutItem>(
            layouts.Select(layout => new EditableLayoutItem(layout.Id, layout.Name, layout.IsActive)));
        LayoutsItemsControl.ItemsSource = _layouts;
    }

    public event EventHandler<LayoutRenameRequestedEventArgs>? RenameRequested;

    public event EventHandler<LayoutDeleteRequestedEventArgs>? DeleteRequested;

    public event EventHandler<LayoutDuplicateRequestedEventArgs>? DuplicateRequested;

    public event EventHandler? AddRequested;

    public void UpdateLayouts(IReadOnlyList<LayoutOverviewItem> layouts)
    {
        _layouts.Clear();
        foreach (var layout in layouts)
            _layouts.Add(new EditableLayoutItem(layout.Id, layout.Name, layout.IsActive));
    }

    private void RenameLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EditableLayoutItem item })
            return;

        var dialog = new LayoutRenameWindow(item.Name)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        RenameRequested?.Invoke(this, new LayoutRenameRequestedEventArgs(item.Id, dialog.LayoutName));
    }

    private void DuplicateLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: EditableLayoutItem item })
            DuplicateRequested?.Invoke(this, new LayoutDuplicateRequestedEventArgs(item.Id));
    }

    private void DeleteLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EditableLayoutItem item })
            return;

        var confirmation = new ConfirmationWindow(
            "Usunąć układ?",
            $"Czy na pewno usunąć układ „{item.Name}”? Tej operacji nie można cofnąć.",
            "Usuń",
            "Anuluj")
        {
            Owner = this
        };

        if (confirmation.ShowDialog() != true)
            return;

        DeleteRequested?.Invoke(this, new LayoutDeleteRequestedEventArgs(item.Id));
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void AddLayoutButton_Click(object sender, RoutedEventArgs e) =>
        AddRequested?.Invoke(this, EventArgs.Empty);

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}

public sealed class EditableLayoutItem : INotifyPropertyChanged
{
    private string _name;

    public EditableLayoutItem(string id, string name, bool isActive)
    {
        Id = id;
        _name = name;
        IsActive = isActive;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (_name == value)
                return;

            _name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public string DisplayName => LocalizationService.T(_name);

    public bool IsActive { get; }

    public Brush RowBackground => new SolidColorBrush(IsActive
        ? Color.FromRgb(0x1F, 0x36, 0x2A)
        : Color.FromRgb(0x20, 0x20, 0x24));

    public Brush RowBorderBrush => new SolidColorBrush(IsActive
        ? Color.FromRgb(0x45, 0x8A, 0x60)
        : Color.FromRgb(0x30, 0x30, 0x35));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record LayoutRenameRequestedEventArgs(string LayoutId, string Name);

public sealed record LayoutDeleteRequestedEventArgs(string LayoutId);
