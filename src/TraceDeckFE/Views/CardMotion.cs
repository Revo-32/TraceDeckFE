using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TraceDeckFE.Models;
using TraceDeckFE.ViewModels;

namespace TraceDeckFE.Views;

public static class CardMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(CardMotion),new PropertyMetadata(false,OnChanged));
    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject obj,bool value) => obj.SetValue(EnabledProperty,value);
    private static void OnChanged(DependencyObject obj,DependencyPropertyChangedEventArgs e)
    {
        if (obj is not Expander expander) return;
        if ((bool)e.OldValue) expander.Expanded -= OnExpanded;
        if ((bool)e.NewValue) expander.Expanded += OnExpanded;
    }
    private static void OnExpanded(object sender,RoutedEventArgs e)
    {
        if (sender is not Expander expander || e.OriginalSource != expander || expander.DataContext is not MainViewModel vm ||
            vm.Settings.Animation == AnimationMode.Off || expander.Template.FindName("ExpandSite",expander) is not FrameworkElement content) return;
        var transform = new TranslateTransform(); content.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(vm.Settings.Animation == AnimationMode.Reduced ? -1 : -4,0,
            TimeSpan.FromMilliseconds(vm.Settings.Animation == AnimationMode.Reduced ? 80 : 160)) { FillBehavior = FillBehavior.Stop });
    }
}
