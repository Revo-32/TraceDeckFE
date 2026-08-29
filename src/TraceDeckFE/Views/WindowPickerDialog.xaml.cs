using System.Windows;
using System.Windows.Input;
using TraceDeckFE.Models;

namespace TraceDeckFE.Views;

public partial class WindowPickerDialog : Window
{
    public WindowPickerDialog(IReadOnlyList<WindowInfo> windows)
    {
        InitializeComponent();
        DataContext = windows;
        if (windows.Count > 0)
        {
            WindowList.SelectedIndex = 0;
        }
    }

    public WindowInfo? SelectedWindow { get; private set; }

    private void OnConnectClick(object sender, RoutedEventArgs e) => AcceptSelection();

    private void OnWindowDoubleClick(object sender, MouseButtonEventArgs e) => AcceptSelection();

    private void AcceptSelection()
    {
        if (WindowList.SelectedItem is not WindowInfo selected)
        {
            return;
        }

        SelectedWindow = selected;
        DialogResult = true;
    }
}
