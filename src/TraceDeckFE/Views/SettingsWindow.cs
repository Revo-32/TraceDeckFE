using TraceDeckFE.Localization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TraceDeckFE.Models;

namespace TraceDeckFE.Views;

public sealed class SettingsWindow : Window
{
    private readonly ApplicationSettings _settings;
    private readonly Func<ShortcutBinding,string?> _probe;
    private readonly StackPanel _page = new() { Margin = new Thickness(22,14,22,22) };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,12,0,0), FontSize = 11 };
    public SettingsWindow(ApplicationSettings settings, Func<ShortcutBinding,string?> probe)
    {
        _settings = settings; _probe = probe; DataContext = settings;
        Title = L.Get("Settings.Title"); Width = 690; Height = 690; MinWidth = 620; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        FontFamily = (FontFamily)FindResource("PretendardFontFamily");
        Background = (Brush)FindResource("WindowBackgroundBrush"); Foreground = (Brush)FindResource("PrimaryTextBrush");
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new GridLength(190) }); root.ColumnDefinitions.Add(new());
        var navigation = new ListBox { Margin = new Thickness(12,16,0,16), Background = (Brush)FindResource("CardBrush"), Foreground = Foreground, BorderThickness = new Thickness(0), ItemContainerStyle = (Style)FindResource("NeutralListItem") };
        foreach (var name in new[] { L.Get("Settings.General"), L.Get("Settings.Interface"), L.Get("Settings.Reference"), L.Get("Settings.ColorPicker"), L.Get("Settings.Shortcuts"), L.Get("Settings.Recovery"), L.Get("Settings.Advanced") })
            navigation.Items.Add(new ListBoxItem { Content = name, Padding = new Thickness(10), FontSize = 12 });
        navigation.SelectionChanged += (_,_) => ShowPage(navigation.SelectedIndex);
        root.Children.Add(navigation);
        var scroll = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll,1); root.Children.Add(scroll); Content = root; navigation.SelectedIndex = 0;
    }
    private void ShowPage(int page)
    {
        _page.Children.Clear(); _notice.Text = "";
        switch (page)
        {
            case 0:
                Heading(L.Get("Settings.General"));
                Choice(L.Get("Settings.Language"),nameof(ApplicationSettings.Language),new[] { (L.Get("Option.System"),AppLanguage.System), ("English",AppLanguage.English), ("한국어",AppLanguage.Korean) });
                Note(L.Get("Settings.RestartLanguage"));
                Choice(L.Get("Settings.Layout"),nameof(ApplicationSettings.Layout),new[] { (L.Get("Option.Auto"),LayoutMode.Auto), (L.Get("Option.LayoutCompact"),LayoutMode.Compact),(L.Get("Option.LayoutWide"),LayoutMode.Wide) });
                Toggle(L.Get("Settings.RememberProject"),nameof(ApplicationSettings.RememberLastProject));
                Toggle(L.Get("Settings.RestoreSession"),nameof(ApplicationSettings.RestorePreviousSession));
                Toggle(L.Get("Settings.AutoDetect"),nameof(ApplicationSettings.AutomaticallyDetectForza));
                Note(L.Get("Settings.SessionNote"));
                break;
            case 1:
                Heading(L.Get("Settings.Interface"));
                Slider(L.Get("Settings.Width"),nameof(ApplicationSettings.ControllerWidth),280,520,1);
                Toggle(L.Get("Settings.RememberWidth"),nameof(ApplicationSettings.RememberWidthPerLayout));
                Toggle(L.Get("Settings.RememberCards"),nameof(ApplicationSettings.RememberFoldedCards));
                Choice(L.Get("Settings.Density"),nameof(ApplicationSettings.Density),Enum.GetValues<UiDensity>().Select(v => (L.Get($"Option.{v}"),v)));
                Choice(L.Get("Settings.Animation"),nameof(ApplicationSettings.Animation),Enum.GetValues<AnimationMode>().Select(v => (L.Get($"Option.{v}"),v)));
                Note(L.Get("Settings.InterfaceNote"));
                break;
            case 2:
                Heading(L.Get("Settings.Reference")); Note(L.Get("Settings.WheelNote"));
                Slider(L.Get("Settings.ZoomStep"),nameof(ApplicationSettings.ZoomStepPercent),1,50,1);
                Toggle(L.Get("Settings.CursorZoom"),nameof(ApplicationSettings.ZoomTowardCursor));
                Slider(L.Get("Settings.ArrowStep"),nameof(ApplicationSettings.ArrowMovement),1,100,1);
                Slider(L.Get("Settings.ShiftArrowStep"),nameof(ApplicationSettings.ShiftArrowMovement),1,100,1);
                Toggle(L.Get("Settings.ConfirmReplacement"),nameof(ApplicationSettings.ConfirmReferenceReplacement));
                break;
            case 3:
                Heading(L.Get("Settings.ColorPicker")); Toggle(L.Get("Ui.Magnifier"),nameof(ApplicationSettings.Magnifier));
                Choice(L.Get("Settings.Precision"),nameof(ApplicationSettings.HsbDecimalPlaces),new[] { ("2",2),("3",3) });
                Note(L.Get("Settings.PrecisionNote"));
                break;
            case 4: BuildShortcuts(); break;
            case 5:
                Heading(L.Get("Settings.Recovery")); Toggle(L.Get("Settings.EnableAutosave"),nameof(ApplicationSettings.AutosaveEnabled));
                Choice(L.Get("Settings.Interval"),nameof(ApplicationSettings.AutosaveIntervalSeconds),new[] { (L.Get("Option.Seconds10"),10),(L.Get("Option.Seconds30"),30),(L.Get("Option.Minute1"),60),(L.Get("Option.Minutes5"),300),(L.Get("Option.Minutes10"),600) });
                Note(L.Get("Settings.AutosaveNote"));
                Note(L.Get("Settings.DiscardNote"));
                break;
            default:
                Heading(L.Get("Settings.Advanced")); Note(L.Get("Settings.AdvancedNote"));
                Note(L.Get("Settings.SafetyNote"));
                break;
        }
        _page.Children.Add(_notice);
    }
    private void Heading(string text) => _page.Children.Add(new TextBlock { Text = text, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,18) });
    private void Note(string text) => _page.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0,8,0,12) });
    private void Toggle(string label, string property)
    {
        var grid = new Grid { Margin = new Thickness(0,7,0,7), ToolTip = L.Get("Help." + property) }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,12,0) });
        var toggle = new ToggleButton { Style = (Style)FindResource("SwitchStyle") };
        System.Windows.Automation.AutomationProperties.SetName(toggle,label);
        toggle.ToolTip = L.Get("Help." + property);
        toggle.SetBinding(ToggleButton.IsCheckedProperty,new Binding(property) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(toggle,1); grid.Children.Add(toggle); _page.Children.Add(grid);
    }
    private void Choice<T>(string label,string property,IEnumerable<(string Label,T Value)> values)
    {
        Note(label);
        var options = values.Select(v => new Option<T>(v.Label,v.Value)).ToArray();
        var combo = new ComboBox { Style = (Style)FindResource("NeutralComboBoxStyle"), ItemsSource = options, DisplayMemberPath = "Label", SelectedValuePath = "Value", FontSize = 13, Padding = new Thickness(7), Margin = new Thickness(0,0,0,12) };
        combo.SetBinding(Selector.SelectedValueProperty,new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        combo.ToolTip = L.Get("Help." + property);
        System.Windows.Automation.AutomationProperties.SetName(combo,label);
        _page.Children.Add(combo);
    }
    private sealed record Option<T>(string Label,T Value)
    {
        public override string ToString() => Label;
    }
    private void Slider(string label,string property,double minimum,double maximum,double step)
    {
        Note(label);
        var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        value.SetBinding(TextBlock.TextProperty,new Binding(property) { StringFormat = "{0:0}" }); _page.Children.Add(value);
        var slider = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = step, IsSnapToTickEnabled = true, Style = (Style)FindResource("NeutralSliderStyle"), Margin = new Thickness(0,0,0,12) };
        slider.ToolTip = L.Get("Help." + property);
        System.Windows.Automation.AutomationProperties.SetName(slider,label);
        slider.SetBinding(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new Binding(property) { Mode = BindingMode.TwoWay }); _page.Children.Add(slider);
    }
    private void BuildShortcuts()
    {
        Heading(L.Get("Settings.Shortcuts")); Note(L.Get("Settings.ShortcutNote"));
        var restore = new Button { Content = L.Get("Settings.RestoreDefaults"), Style = (Style)FindResource("NeutralButtonStyle"), Margin = new Thickness(0,0,0,12) };
        restore.ToolTip = L.Get("Tip.RestoreShortcuts");
        restore.Click += (_,_) =>
        {
            var dialog = new ChoiceDialog(L.Get("Dialog.RestoreShortcuts"), L.Get("Dialog.RestoreQuestion"), L.Get("Ui.Restore"), L.Get("Ui.Cancel")) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice == 0) { _settings.Shortcuts = ShortcutCatalog.Defaults(); ShowPage(4); }
        };
        _page.Children.Add(restore);
        foreach (var binding in _settings.Shortcuts)
        {
            var row = new Grid { Margin = new Thickness(0,3,0,3) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(165) });
            row.Children.Add(new TextBlock { Text = L.Get($"Action.{binding.Action}"), VerticalAlignment = VerticalAlignment.Center, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,8,0) });
            var button = new Button { Content = binding.Gesture, Tag = binding.Action, Style = (Style)FindResource("NeutralButtonStyle"), FontSize = 11 };
            button.ToolTip = L.Get("Tip.Shortcut");
            System.Windows.Automation.AutomationProperties.SetName(button,L.Get($"Action.{binding.Action}"));
            bool capturing = false;
            button.Click += (_,_) => { capturing = true; button.Content = L.Get("Settings.PressKeys"); button.Focus(); };
            button.LostKeyboardFocus += (_,_) => { capturing = false; button.Content = _settings.Shortcuts.First(b => b.Action == binding.Action).Gesture; };
            button.PreviewKeyDown += (_,e) =>
            {
                if (!capturing) return;
                e.Handled = true; var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin) return;
                var candidate = new ShortcutBinding(binding.Action,key,Keyboard.Modifiers);
                var error = ShortcutCatalog.Validate(candidate,_settings.Shortcuts) ?? _probe(candidate);
                if (error is not null) { _notice.Text = error; return; }
                _settings.Shortcuts = _settings.Shortcuts.Select(b => b.Action == candidate.Action ? candidate : b).ToList();
                button.Content = candidate.Gesture; capturing = false; _notice.Text = L.Get("Settings.ShortcutUpdated");
            };
            Grid.SetColumn(button,1); row.Children.Add(button); _page.Children.Add(row);
        }
    }
}
