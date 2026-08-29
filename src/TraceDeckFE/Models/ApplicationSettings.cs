using System.Runtime.CompilerServices;

namespace TraceDeckFE.Models;

public enum LayoutMode { Auto, Compact, Wide }
public enum UiDensity { Automatic, Comfortable, Compact }
public enum AnimationMode { Normal, Reduced, Off }
public enum AppLanguage { System, English, Korean }

public static class LayoutPolicy
{
    // WPF usable workspace in DIPs: wide requires both space and an ultrawide-shaped work area.
    public const double WideMinimumWidth = 2200;
    public const double WideMinimumAspect = 2.1;
    public const double MinimumWidth = 280;
    public const double MaximumWidth = 520;
    public static LayoutMode Resolve(LayoutMode mode, double width, double height) => mode != LayoutMode.Auto ? mode :
        width >= WideMinimumWidth && height > 0 && width / height >= WideMinimumAspect ? LayoutMode.Wide : LayoutMode.Compact;
    public static double ClampWidth(double width) => Math.Clamp(double.IsFinite(width) ? width : 312, MinimumWidth, MaximumWidth);
    public static double DefaultWidth(LayoutMode mode) => mode == LayoutMode.Wide ? 448 : 312;
}

public sealed class ApplicationSettings : ObservableObject
{
    private LayoutMode _layout;
    private bool _rememberLastProject = true, _restoreSession = true, _autoDetect = true, _rememberWidths = true, _rememberCards = true;
    private double _compactWidth = 312, _wideWidth = 448, _width = 312, _zoom = 10;
    private UiDensity _density;
    private AnimationMode _animation;
    private AppLanguage _language;
    private bool _cursorZoom = true, _confirmReplacement = true, _magnifier = true, _autosave = true;
    private int _arrow = 1, _shiftArrow = 10, _precision = 3, _interval = 300;
    private string? _lastProject;
    private ProjectUiStateData _cards = new();
    private List<ShortcutBinding> _shortcuts = ShortcutCatalog.Defaults();
    public LayoutMode Layout { get => _layout; set => Set(ref _layout, Valid(value)); }
    public bool RememberLastProject { get => _rememberLastProject; set => Set(ref _rememberLastProject, value); }
    public bool RestorePreviousSession { get => _restoreSession; set => Set(ref _restoreSession, value); }
    public bool AutomaticallyDetectForza { get => _autoDetect; set => Set(ref _autoDetect, value); }
    public bool RememberWidthPerLayout { get => _rememberWidths; set => Set(ref _rememberWidths, value); }
    public bool RememberFoldedCards { get => _rememberCards; set => Set(ref _rememberCards, value); }
    public double CompactWidth { get => _compactWidth; set => Set(ref _compactWidth, LayoutPolicy.ClampWidth(value)); }
    public double WideWidth { get => _wideWidth; set => Set(ref _wideWidth, LayoutPolicy.ClampWidth(value)); }
    public double ControllerWidth { get => _width; set => Set(ref _width, LayoutPolicy.ClampWidth(value)); }
    public UiDensity Density { get => _density; set => Set(ref _density, Valid(value)); }
    public AnimationMode Animation { get => _animation; set => Set(ref _animation, Valid(value)); }
    public AppLanguage Language { get => _language; set => Set(ref _language, Valid(value)); }
    public double ZoomStepPercent { get => _zoom; set => Set(ref _zoom, Math.Clamp(double.IsFinite(value) ? value : 10, 1, 50)); }
    public bool ZoomTowardCursor { get => _cursorZoom; set => Set(ref _cursorZoom, value); }
    public int ArrowMovement { get => _arrow; set => Set(ref _arrow, Math.Clamp(value, 1, 100)); }
    public int ShiftArrowMovement { get => _shiftArrow; set => Set(ref _shiftArrow, Math.Clamp(value, 1, 100)); }
    public bool ConfirmReferenceReplacement { get => _confirmReplacement; set => Set(ref _confirmReplacement, value); }
    public bool Magnifier { get => _magnifier; set => Set(ref _magnifier, value); }
    public int HsbDecimalPlaces { get => _precision; set => Set(ref _precision, value == 2 ? 2 : 3); }
    public bool AutosaveEnabled { get => _autosave; set => Set(ref _autosave, value); }
    public int AutosaveIntervalSeconds { get => _interval; set => Set(ref _interval, AllowedIntervals.Contains(value) ? value : 300); }
    public string? LastProjectPath { get => _lastProject; set => Set(ref _lastProject, value); }
    public ProjectUiStateData FoldedCards { get => _cards; set => Set(ref _cards, value ?? new()); }
    public List<ShortcutBinding> Shortcuts { get => _shortcuts; set => Set(ref _shortcuts, ShortcutCatalog.Sanitize(value)); }
    public static int[] AllowedIntervals => [10, 30, 60, 300, 600];
    public double WidthFor(LayoutMode mode) => RememberWidthPerLayout ? mode == LayoutMode.Wide ? WideWidth : CompactWidth : ControllerWidth;
    public void RememberWidth(LayoutMode mode, double width)
    {
        ControllerWidth = width;
        if (RememberWidthPerLayout) { if (mode == LayoutMode.Wide) WideWidth = width; else CompactWidth = width; }
    }
    private static T Valid<T>(T value) where T : struct, Enum => Enum.IsDefined(value) ? value : default;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null) => SetProperty(ref field, value, property);
}
