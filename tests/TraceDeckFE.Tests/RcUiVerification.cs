using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TraceDeckFE.Localization;
using TraceDeckFE.Models;
using TraceDeckFE.ViewModels;
using TraceDeckFE.Views;

namespace TraceDeckFE.Tests;

[CollectionDefinition("Wpf UI", DisableParallelization = true)]
public sealed class WpfUiCollection { }

internal static class RcUiVerification
{
    // Runs on the existing integration Dispatcher. No native input injection or second Application.
    public static void Run(MainViewModel vm, Application app)
    {
        var snapshot = ProjectEditSnapshot.Capture(vm.Reference, vm.Guides, vm.Colors, vm.Palette);
        var history = vm.History.UndoCount;
        var culture = CultureInfo.CurrentCulture;
        using (var stream=Application.GetResourceStream(new Uri("/TraceDeckFE;component/Assets/TraceDeck_FE_Mini_logo.png",UriKind.Relative)).Stream)
            Assert.Equal("E658C20C0852B70378638881C882FA5A954322A238BB5AA5C6B871F630B73F9F",Convert.ToHexString(SHA256.HashData(stream)));
        Assert.IsType<CroppedBitmap>(app.FindResource("BrandLogo"));
        var help=new ToolTip {Content=L.Get("Tip.Lock"),Style=(Style)app.FindResource(typeof(ToolTip))};
        Layout(help,320,90);help.ApplyTemplate();
        Assert.Equal(new CornerRadius(7),Assert.IsType<Border>(help.Template.FindName("HelpSurface",help)).CornerRadius);
        Assert.Equal(320,help.MaxWidth);
        foreach(var language in new[]{AppLanguage.Korean,AppLanguage.English})
        {
            L.Initialize(language);
            Assert.Same(culture,CultureInfo.CurrentCulture); // HSB/serialization numeric culture is not changed.
            var window=new MainWindow(vm);
            foreach(var layout in new[]{LayoutMode.Compact,LayoutMode.Wide,LayoutMode.Auto})
            foreach(var width in new[]{280d,312d,448d,520d})
            {
                vm.Settings.Layout=layout; vm.UiState.ControllerWidth=width;
                var root=(FrameworkElement)window.Content;
                Layout(root,width,900);
                var gear=(Button)window.FindName("SettingsButton");
                Assert.Equal(28,gear.ActualWidth);
                Assert.Equal(30,gear.ActualHeight);
                Assert.Equal(L.Get("Tip.Settings"),gear.ToolTip);
                Assert.Equal(450,ToolTipService.GetInitialShowDelay(gear));
                Assert.Equal(12000,ToolTipService.GetShowDuration(gear));
                var icon=Descendants<Viewbox>(gear).Single();
                Assert.InRange(icon.ActualWidth,12,16);Assert.InRange(icon.ActualHeight,12,16);
                var scroll=Descendants<ScrollViewer>(root).First();
                scroll.ApplyTemplate();
                var bar=Assert.IsType<ScrollBar>(scroll.Template.FindName("PART_VerticalScrollBar",scroll));
                Assert.Same(app.FindResource("NeutralScrollBar"),bar.Style);
                Assert.Equal(10,bar.Width);
                if(bar.Visibility==Visibility.Visible) Assert.Equal(10,bar.ActualWidth);
                bar.ApplyTemplate();
                var track=Assert.IsType<Track>(bar.Template.FindName("PART_Track",bar));
                track.Thumb.ApplyTemplate();
                Assert.IsType<Border>(track.Thumb.Template.FindName("ThumbBody",track.Thumb));
                Assert.Contains(Descendants<TextBlock>(root),t=>t.Text==L.Get("Card.Project"));
                Assert.All(Descendants<Button>(root).Where(b=>b.Content is string text && text==L.Get("Ui.Save")),
                    b=>Assert.Equal(L.Get("Tip.Save"),b.ToolTip));
                if (layout==LayoutMode.Compact && width==280 || layout==LayoutMode.Wide && width==448)
                    RenderProof(root,$"{language}-{layout}-{width}");
            }
            // This window was never shown: detach events without disposing the shared VM.
            typeof(MainWindow).GetMethod("DisposeReliabilityUi",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,null);
            var handler=typeof(MainWindow).GetMethod("OnFirstTargetConnected",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            vm.FirstTargetConnected-=(EventHandler<IntRect>)handler.CreateDelegate(typeof(EventHandler<IntRect>),window);
            var settings=new SettingsWindow(vm.Settings,_=>null);
            var settingsRoot=(FrameworkElement)settings.Content;
            Layout(settingsRoot,690,690);
            var navigation=Descendants<ListBox>(settingsRoot).Single();
            for(var page=0;page<7;page++)
            {
                navigation.SelectedIndex=page;Layout(settingsRoot,690,690);
                Assert.DoesNotContain(Descendants<TextBlock>(settingsRoot),t=>t.Text.StartsWith("Settings.") || t.Text.StartsWith("Action."));
                Assert.All(Descendants<ToggleButton>(settingsRoot).Where(t=>t.ToolTip is string),t=>Assert.DoesNotContain("Help.",(string)t.ToolTip));
                RenderProof(settingsRoot,$"{language}-Settings-{page}");
            }
            navigation.SelectedIndex=0;Layout(settingsRoot,690,690);
            var combo=Descendants<ComboBox>(settingsRoot).First();
            var active=L.Culture;
            combo.SelectedValue=language==AppLanguage.English?AppLanguage.Korean:AppLanguage.English;
            Assert.Same(active,L.Culture); // Preference is restart-only; no live-resource subscriptions.
            Assert.Contains(Descendants<TextBlock>(settingsRoot),t=>t.Text==L.Get("Settings.RestartLanguage"));
            var unsaved=new UnsavedChangesDialog();Layout((FrameworkElement)unsaved.Content,420,190);
            Assert.Equal(L.Get("Dialog.UnsavedTitle"),unsaved.Title);
            var after=ProjectEditSnapshot.Capture(vm.Reference,vm.Guides,vm.Colors,vm.Palette);
            Assert.Equal(snapshot.Fingerprint,after.Fingerprint);
            Assert.Same(snapshot.Source,after.Source);
            Assert.Equal(history,vm.History.UndoCount);
        }
        L.Initialize(AppLanguage.English);
    }

    private static void Layout(FrameworkElement root,double width,double height)
    {
        root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
    }
    private static void RenderProof(FrameworkElement root,string name)
    {
        if(Environment.GetEnvironmentVariable("TRACEDECK_RC_RENDER_DIRECTORY") is not {Length:>0} directory) return;
        Directory.CreateDirectory(directory);
        var target=new RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,PixelFormats.Pbgra32);
        target.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));
        using var file=File.Create(Path.Combine(directory,name+".png"));encoder.Save(file);
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);
            if(child is T match)yield return match;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
