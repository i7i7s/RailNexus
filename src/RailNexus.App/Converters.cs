using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RailNexus.App.Core;

namespace RailNexus.App;

/// <summary>Warna pendukung status dari kanvas desain. Status selalu tetap memakai label teks.</summary>
public sealed class ToneBrushConverter : IValueConverter
{
    /// <summary>true = warna teks, false = warna latar.</summary>
    public bool Foreground { get; set; }

    private static readonly Dictionary<Tone, (string Bg, string Fg)> Map = new()
    {
        [Tone.Ok] = ("#E3F2E8", "#17603A"),
        [Tone.Warn] = ("#FBEFD5", "#784C00"),
        [Tone.Err] = ("#FBE6E3", "#A3201A"),
        [Tone.Info] = ("#E4ECFA", "#163F87"),
        [Tone.Mute] = ("#ECEEF1", "#48505A"),
        [Tone.Unknown] = ("#ECE8F8", "#4B3591"),
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var (bg, fg) = Map[value is Tone t ? t : Tone.Mute];
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Foreground ? fg : bg));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>null, string kosong, false, atau koleksi kosong → Collapsed. Parameter "invert" membalik.</summary>
public sealed class VisibleWhenConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            bool b => b,
            string s => s.Length > 0,
            int n => n > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        if (parameter as string == "invert") visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Enum ↔ indeks, untuk TabControl.SelectedIndex.</summary>
public sealed class EnumIndexConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? System.Convert.ToInt32(e, CultureInfo.InvariantCulture) : 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && parameter is Type t ? Enum.ToObject(t, i) : Binding.DoNothing;
}
