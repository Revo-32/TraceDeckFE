using System.Windows;
using TraceDeckFE.Localization;

namespace TraceDeckFE.Views;

public partial class ReferenceReplacementDialog : Window
{
    public ReferenceReplacementDialog(string currentName, string incomingName)
    {
        InitializeComponent();
        MessageText.Text = L.Format("Dialog.ReplaceMessage", currentName, incomingName);
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
