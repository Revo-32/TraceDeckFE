using TraceDeckFE.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;

namespace TraceDeckFE.Models;

public readonly record struct RgbaColor(byte Red, byte Green, byte Blue, byte Alpha = 255)
{
    public string HexRgb => $"#{Red:X2}{Green:X2}{Blue:X2}";
    public string HexRgba => $"#{Red:X2}{Green:X2}{Blue:X2}{Alpha:X2}";
}

public readonly record struct ForzaHsb(double Hue, double Saturation, double Brightness)
{
    public string ToDisplayString(int precision = 3)
    {
        var format = $"F{Math.Clamp(precision, 0, 9)}";
        return string.Join(" / ",
            Hue.ToString(format, CultureInfo.InvariantCulture),
            Saturation.ToString(format, CultureInfo.InvariantCulture),
            Brightness.ToString(format, CultureInfo.InvariantCulture));
    }
}

public static class ForzaColorConverter
{
    public static ForzaHsb FromRgb(RgbaColor color)
    {
        var red = color.Red / 255.0;
        var green = color.Green / 255.0;
        var blue = color.Blue / 255.0;
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        var delta = maximum - minimum;

        double hue;
        if (delta <= double.Epsilon)
        {
            hue = 0;
        }
        else if (maximum == red)
        {
            hue = ((green - blue) / delta) % 6.0;
        }
        else if (maximum == green)
        {
            hue = (blue - red) / delta + 2.0;
        }
        else
        {
            hue = (red - green) / delta + 4.0;
        }

        hue /= 6.0;
        if (hue < 0)
        {
            hue += 1.0;
        }

        var saturation = maximum <= double.Epsilon ? 0 : delta / maximum;
        return new ForzaHsb(hue, saturation, maximum);
    }
}

public sealed class ColorState : ObservableObject
{
    public const int DisplayPrecision = 3;
    private RgbaColor? _current;
    private bool _magnifierEnabled = true;
    private bool _isPicking;
    private int _precision = DisplayPrecision;
    public int Precision
    {
        get => _precision;
        set
        {
            if (!SetProperty(ref _precision, value == 2 ? 2 : 3)) return;
            OnPropertyChanged(nameof(HueText)); OnPropertyChanged(nameof(SaturationText));
            OnPropertyChanged(nameof(BrightnessText)); OnPropertyChanged(nameof(HsbText));
        }
    }

    public event EventHandler? ContentChanged;

    public RgbaColor? Current
    {
        get => _current;
        private set
        {
            if (!SetProperty(ref _current, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasColor));
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(Rgb));
            OnPropertyChanged(nameof(Alpha));
            OnPropertyChanged(nameof(AlphaVisibilityText));
            OnPropertyChanged(nameof(IsTransparent));
            OnPropertyChanged(nameof(Hsb));
            OnPropertyChanged(nameof(HueText));
            OnPropertyChanged(nameof(SaturationText));
            OnPropertyChanged(nameof(BrightnessText));
            OnPropertyChanged(nameof(HsbText));
            OnPropertyChanged(nameof(SwatchBrush));
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool MagnifierEnabled
    {
        get => _magnifierEnabled;
        set
        {
            if (SetProperty(ref _magnifierEnabled, value))
            {
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsPicking
    {
        get => _isPicking;
        internal set
        {
            if (SetProperty(ref _isPicking, value))
            {
                OnPropertyChanged(nameof(PickButtonText));
            }
        }
    }

    public bool HasColor => Current.HasValue;
    public string Hex => Current is { } color ? color.HexRgb : "—";
    public string Rgb => Current is { } color ? $"{color.Red}   {color.Green}   {color.Blue}" : "—";
    public string Alpha => Current is { Alpha: < 255 } color ? color.Alpha.ToString(CultureInfo.InvariantCulture) : string.Empty;
    public string AlphaVisibilityText => Current is { Alpha: < 255 } color ? L.Format("Status.Alpha", color.Alpha) : string.Empty;
    public bool IsTransparent => Current is { Alpha: 0 };
    public ForzaHsb Hsb => Current is { } color ? ForzaColorConverter.FromRgb(color) : default;
    public string HueText => Hsb.Hue.ToString($"F{Precision}", CultureInfo.InvariantCulture);
    public string SaturationText => Hsb.Saturation.ToString($"F{Precision}", CultureInfo.InvariantCulture);
    public string BrightnessText => Hsb.Brightness.ToString($"F{Precision}", CultureInfo.InvariantCulture);
    public string HsbText => HasColor ? Hsb.ToDisplayString(Precision) : "—";
    public string PickButtonText => IsPicking ? L.Get("Ui.CancelPicker") : L.Get("Ui.PickColor");
    public Brush SwatchBrush
    {
        get
        {
            var color = Current ?? new RgbaColor(48, 48, 48);
            var brush = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
            brush.Freeze();
            return brush;
        }
    }

    public void SetColor(RgbaColor color) => Current = color;
    public void Clear() => Current = null;

    public void Restore(RgbaColor? color, bool magnifierEnabled)
    {
        _current = color;
        _magnifierEnabled = magnifierEnabled;
        IsPicking = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(HasColor));
        OnPropertyChanged(nameof(Hex));
        OnPropertyChanged(nameof(Rgb));
        OnPropertyChanged(nameof(Alpha));
        OnPropertyChanged(nameof(AlphaVisibilityText));
        OnPropertyChanged(nameof(IsTransparent));
        OnPropertyChanged(nameof(Hsb));
        OnPropertyChanged(nameof(HueText));
        OnPropertyChanged(nameof(SaturationText));
        OnPropertyChanged(nameof(BrightnessText));
        OnPropertyChanged(nameof(HsbText));
        OnPropertyChanged(nameof(SwatchBrush));
        OnPropertyChanged(nameof(MagnifierEnabled));
    }

    public static string FormatComponent(double value) =>
        value.ToString($"F{DisplayPrecision}", CultureInfo.InvariantCulture);
}

public sealed class PaletteItem : ObservableObject
{
    private string _name;
    private int _precision = 3;
    public int Precision { get => _precision; set { if (SetProperty(ref _precision,value == 2 ? 2 : 3)) OnPropertyChanged(nameof(HsbText)); } }

    public PaletteItem(Guid id, string name, RgbaColor color, bool isGenerated = false)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        _name = NormalizeName(name);
        Color = color;
        IsGenerated = isGenerated;
    }

    public event EventHandler? ItemChanged;
    public Guid Id { get; }
    public RgbaColor Color { get; }
    public bool IsGenerated { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, NormalizeName(value)))
            {
                ItemChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Hex => Color.HexRgb;
    public string HsbText => ForzaColorConverter.FromRgb(Color).ToDisplayString(Precision);
    public Brush SwatchBrush
    {
        get
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(Color.Alpha, Color.Red, Color.Green, Color.Blue));
            brush.Freeze();
            return brush;
        }
    }

    private static string NormalizeName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Untitled Color" : value.Trim()[..Math.Min(value.Trim().Length, 80)];
}

public sealed class PaletteState : ObservableObject
{
    private int _autoColorCount = 6;
    private int _precision = 3;
    public int Precision { get => _precision; set { _precision = value == 2 ? 2 : 3; foreach (var item in Items) item.Precision = _precision; } }

    public PaletteState()
    {
        Items = new ObservableCollection<PaletteItem>();
    }

    public event EventHandler? ContentChanged;
    public ObservableCollection<PaletteItem> Items { get; }

    public int AutoColorCount
    {
        get => _autoColorCount;
        set
        {
            if (SetProperty(ref _autoColorCount, Math.Clamp(value, 2, 12)))
            {
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public PaletteItem Add(RgbaColor color, string? name = null, bool isGenerated = false)
    {
        var item = new PaletteItem(Guid.NewGuid(), name ?? DefaultName(color, isGenerated), color, isGenerated);
        Subscribe(item);
        Items.Add(item);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public bool Delete(PaletteItem item)
    {
        if (!Items.Remove(item))
        {
            return false;
        }

        Unsubscribe(item);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Move(PaletteItem item, int newIndex)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0)
        {
            return false;
        }

        newIndex = Math.Clamp(newIndex, 0, Items.Count - 1);
        if (newIndex == oldIndex)
        {
            return false;
        }

        Items.Move(oldIndex, newIndex);
        ContentChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ReplaceAll(IEnumerable<PaletteItem> items, int autoColorCount = 6)
    {
        foreach (var existing in Items)
        {
            Unsubscribe(existing);
        }

        Items.Clear();
        foreach (var item in items)
        {
            Subscribe(item);
            Items.Add(item);
        }

        _autoColorCount = Math.Clamp(autoColorCount, 2, 12);
        OnPropertyChanged(nameof(AutoColorCount));
    }

    public void Clear() => ReplaceAll(Array.Empty<PaletteItem>());

    private void Subscribe(PaletteItem item) { item.Precision = Precision; item.ItemChanged += OnItemChanged; }
    private void Unsubscribe(PaletteItem item) => item.ItemChanged -= OnItemChanged;
    private void OnItemChanged(object? sender, EventArgs e) => ContentChanged?.Invoke(this, EventArgs.Empty);
    private static string DefaultName(RgbaColor color, bool generated) =>
        generated ? L.Format("Palette.GeneratedName", color.HexRgb) : color.HexRgb;
}
