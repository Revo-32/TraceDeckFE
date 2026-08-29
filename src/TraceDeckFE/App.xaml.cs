using System.Windows;
using TraceDeckFE.Localization;
using TraceDeckFE.Models;
using TraceDeckFE.Overlay;
using TraceDeckFE.Services;
using TraceDeckFE.ViewModels;

namespace TraceDeckFE;

public partial class App : Application
{
    private MainViewModel? _mainViewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new PortableApplicationPaths();
        var logger = new TraceLogger(paths);
        var settingsService = new SettingsService(paths);
        var settings = settingsService.Load(out var settingsWarning);
        L.Initialize(settings.Language);
        var recovery = new RecoveryService(paths, logger);
        DispatcherUnhandledException += (_, args) =>
        {
            logger.Error("Unhandled UI exception", args.Exception);
            _mainViewModel?.Notify(L.Get("Notice.Unexpected"));
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Error("Unhandled application exception", args.ExceptionObject as Exception);

        var reference = new ReferenceState();
        var guides = new GuideState();
        var inputSettings = new ReferenceInputSettings();
        var colors = new ColorState();
        var palette = new PaletteState();
        var project = new ProjectSession();
        var uiState = new ProjectUiState();
        var catalog = new WindowCatalog(logger);
        var tracker = new ForzaWindowTracker(logger);
        var imageService = new ReferenceImageService(logger);
        var colorService = new ReferenceColorService(imageService);
        var autoPaletteService = new AutoPaletteService(colorService);
        var projectService = new ProjectArchiveService();
        var overlay = new OverlayWindow(reference, guides, colors, colorService, logger);

        _mainViewModel = new MainViewModel(
            reference, guides, inputSettings, colors, palette, project, uiState,
            catalog, tracker, imageService, autoPaletteService, projectService, overlay, logger);
        _mainViewModel.ConfigureReliability(settings, settingsService, recovery);
        if (settingsWarning is not null) _mainViewModel.Notify(L.Get(settingsWarning));
        var mainWindow = new MainWindow(_mainViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();

        overlay.InitializeHidden();
        _mainViewModel.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mainViewModel?.Dispose();
        base.OnExit(e);
    }
}
