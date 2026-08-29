using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TraceDeckFE.Localization;
using TraceDeckFE.Models;
using TraceDeckFE.ViewModels;
using TraceDeckFE.Views;

namespace TraceDeckFE.Tests;

internal static class RcLifecycleVerification
{
    // In-process WPF lifetime test, on the existing integration Dispatcher.
    // Does not send native input, capture the desktop, or launch another Application.
    public static async Task RunAsync(MainViewModel vm)
    {
        var before = ProjectEditSnapshot.Capture(vm.Reference, vm.Guides, vm.Colors, vm.Palette);
        var undo = vm.History.UndoCount;
        var redo = vm.History.RedoCount;
        var language = vm.Settings.Language;
        var activeLanguage = L.Culture.TwoLetterISOLanguageName == "ko" ? AppLanguage.Korean : AppLanguage.English;
        var windows = new List<WeakReference>();
        var controls = new List<WeakReference>();
        try
        {
            foreach (var displayLanguage in new[] { AppLanguage.Korean, AppLanguage.English })
            {
                L.Initialize(displayLanguage);
                for (var iteration = 0; iteration < 12; iteration++)
                {
                    var references = CreateVisitAndClose(vm.Settings);
                    windows.Add(references.Window);
                    controls.Add(references.LanguageControl);
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
            }

            // Drain deferred WPF binding/layout cleanup before checking reachability.
            // Check objects, not an arbitrary process-memory threshold affected by caches/JIT.
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (windows.All(reference => !reference.IsAlive) && controls.All(reference => !reference.IsAlive)) break;
                await Task.Delay(20);
            }
            Assert.All(windows, reference => Assert.False(reference.IsAlive, "A closed Settings window is retained."));
            Assert.All(controls, reference => Assert.False(reference.IsAlive, "A replaced language control is retained."));
            var after = ProjectEditSnapshot.Capture(vm.Reference, vm.Guides, vm.Colors, vm.Palette);
            Assert.Equal(before.Fingerprint, after.Fingerprint);
            Assert.Same(before.Source, after.Source);
            Assert.Equal(undo, vm.History.UndoCount);
            Assert.Equal(redo, vm.History.RedoCount);
        }
        finally
        {
            vm.Settings.Language = language;
            L.Initialize(activeLanguage);
            await vm.FlushSettingsAsync();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Window, WeakReference LanguageControl) CreateVisitAndClose(ApplicationSettings settings)
    {
        var window = new SettingsWindow(settings, static _ => null);
        var root = (FrameworkElement)window.Content;
        Layout(root);
        var navigation = Find<ListBox>(root).Single();
        var language = Find<ComboBox>(root).First();
        var active = L.Culture;
        var selected = settings.Language == AppLanguage.Korean ? AppLanguage.English : AppLanguage.Korean;
        language.SelectedValue = selected;
        Assert.Equal(selected, settings.Language);
        Assert.Same(active, L.Culture);
        var references = (new WeakReference(window), new WeakReference(language));
        for (var page = 1; page < 7; page++)
        {
            navigation.SelectedIndex = page;
            Layout(root);
        }
        navigation.SelectedIndex = 0;
        Layout(root);
        Assert.Equal(selected, Find<ComboBox>(root).First().SelectedValue);
        window.Close(); // Actual Window lifecycle; no manual clearing of bindings/events/content.
        return references;
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(690, 690));
        root.Arrange(new Rect(0, 0, 690, 690));
        root.UpdateLayout();
    }

    private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var descendant in Find<T>(child)) yield return descendant;
        }
    }
}
