using System.ComponentModel;
using System.Windows.Input;

namespace RailNexus.App.Core.ViewModels;

/// <summary>Halaman di area kerja tengah. Bar bawah menampilkan satu tindakan utama milik halaman (PRD 6.1).</summary>
public interface IPage : INotifyPropertyChanged
{
    string Title { get; }
    FooterAction Footer { get; }
    /// <summary>Label tombol kembali; null bila halaman tidak punya tujuan kembali.</summary>
    string? BackLabel { get; }
    void Back();
}

/// <summary>Tindakan utama di bar bawah beserta keterangan mengapa tombol aktif/nonaktif (PRD 6.3).</summary>
public sealed class FooterAction(string label, ICommand command, Func<string> hint) : ObservableObject
{
    public string Label => label;
    public ICommand Command => command;
    public string Hint => hint();
    public bool IsEnabled => command.CanExecute(null);

    public void Refresh()
    {
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(IsEnabled));
    }
}

/// <summary>Butir navigasi kiri. Modul yang belum tersedia tetap tampil dengan alasan, tidak disembunyikan.</summary>
public sealed class NavItem(string key, string label, string? disabledReason) : ObservableObject
{
    private bool _isActive;
    private string? _disabledReason = disabledReason;

    public string Key => key;
    public string Label => label;
    public bool IsEnabled => _disabledReason is null;
    public string? DisabledReason
    {
        get => _disabledReason;
        set { if (Set(ref _disabledReason, value)) OnPropertyChanged(nameof(IsEnabled)); }
    }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}
