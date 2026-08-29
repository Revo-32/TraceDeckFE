using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TraceDeckFE.Views;

public sealed class ChoiceDialog : Window
{
    private readonly List<Button> _buttons = [];
    public void SetChoiceHelp(params string[] help)
    {
        for (var i = 0; i < Math.Min(help.Length, _buttons.Count); i++) _buttons[i].ToolTip = help[i];
    }
    public int Choice { get; private set; } = -1;
    public ChoiceDialog(string title, string message, params string[] choices)
    {
        Title = title; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = (FontFamily)FindResource("PretendardFontFamily");
        Background = (Brush)FindResource("WindowBackgroundBrush"); Foreground = (Brush)FindResource("PrimaryTextBrush");
        var content = new StackPanel { Margin = new Thickness(22) };
        content.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 15, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,14,0,18) });
        var buttons = new System.Windows.Controls.Primitives.UniformGrid { Columns = choices.Length };
        for (var i = 0; i < choices.Length; i++)
        {
            var index = i;
            var button = new Button { Content = choices[i], Margin = new Thickness(3), Style = (Style)FindResource("NeutralButtonStyle") };
            button.Click += (_,_) => { Choice = index; DialogResult = true; };
            _buttons.Add(button);
            buttons.Children.Add(button);
        }
        content.Children.Add(buttons); Content = content;
    }
}
