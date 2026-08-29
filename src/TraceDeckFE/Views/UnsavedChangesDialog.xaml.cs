using System.Windows;

namespace TraceDeckFE.Views;

public enum UnsavedChangesChoice
{
    Cancel,
    Save,
    DontSave
}

public partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedChangesChoice Choice { get; private set; } = UnsavedChangesChoice.Cancel;

    private void OnSaveClick(object sender, RoutedEventArgs e) { Choice = UnsavedChangesChoice.Save; DialogResult = true; }
    private void OnDontSaveClick(object sender, RoutedEventArgs e) { Choice = UnsavedChangesChoice.DontSave; DialogResult = true; }
    private void OnCancelClick(object sender, RoutedEventArgs e) { Choice = UnsavedChangesChoice.Cancel; DialogResult = false; }
}
