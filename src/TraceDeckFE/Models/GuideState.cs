namespace TraceDeckFE.Models;

public sealed class GuideState : ObservableObject
{
    private bool _isGridVisible;
    private double _gridSpacing = 100;
    private double _opacity = 0.28;
    private bool _isHorizontalCenterVisible;
    private bool _isVerticalCenterVisible;

    public bool IsGridVisible
    {
        get => _isGridVisible;
        set
        {
            if (SetProperty(ref _isGridVisible, value))
            {
                OnPropertyChanged(nameof(HasVisibleGuide));
            }
        }
    }

    public double GridSpacing
    {
        get => _gridSpacing;
        set => SetProperty(ref _gridSpacing, double.IsFinite(value) ? Math.Clamp(value, 16, 400) : 100);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, double.IsFinite(value) ? Math.Clamp(value, 0.05, 1) : 0.28);
    }

    public bool IsHorizontalCenterVisible
    {
        get => _isHorizontalCenterVisible;
        set
        {
            if (SetProperty(ref _isHorizontalCenterVisible, value))
            {
                OnPropertyChanged(nameof(HasVisibleGuide));
            }
        }
    }

    public bool IsVerticalCenterVisible
    {
        get => _isVerticalCenterVisible;
        set
        {
            if (SetProperty(ref _isVerticalCenterVisible, value))
            {
                OnPropertyChanged(nameof(HasVisibleGuide));
            }
        }
    }

    public bool HasVisibleGuide => IsGridVisible || IsHorizontalCenterVisible || IsVerticalCenterVisible;

    public void Restore(GuideProjectState? state)
    {
        state ??= new GuideProjectState();
        IsGridVisible = state.GridEnabled;
        GridSpacing = state.GridSpacing;
        Opacity = state.Opacity;
        IsHorizontalCenterVisible = state.HorizontalCenterGuide;
        IsVerticalCenterVisible = state.VerticalCenterGuide;
    }

    public void Reset() => Restore(new GuideProjectState());
}
