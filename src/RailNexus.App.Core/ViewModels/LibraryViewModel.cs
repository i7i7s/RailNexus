using System.Collections.ObjectModel;
using System.Globalization;
using RailNexus.Core;
using RailNexus.Core.Library;

namespace RailNexus.App.Core.ViewModels;

public sealed class AssetRow(LibraryEntry entry)
{
    public LibraryEntry Entry => entry;
    public string Name => entry.Name;
    public string KuidText => entry.Kuid.ToString();
    /// <summary>Kategori Trainz apa adanya, mis. "VE;LND"; belum diterjemahkan ke jenis aset.</summary>
    public string Category => entry.Category;
    public StatusLabel Status => Labels.Asset(entry.State);
    public IReadOnlyList<StatusLabel> Extra => Labels.Extra(entry);
    public string CheckedAt => "Diperiksa " + entry.CheckedAt.ToLocalTime().ToString("d MMM yyyy HH.mm", CultureInfo.GetCultureInfo("id-ID"));
}

public sealed class FilterChip(LibraryFilter filter) : ObservableObject
{
    private long _count;
    private bool _isSelected;

    public LibraryFilter Filter => filter;
    public long Count { get => _count; set { if (Set(ref _count, value)) OnPropertyChanged(nameof(Label)); } }
    public string Label => filter == LibraryFilter.All ? Labels.Filter(filter) : $"{Labels.Filter(filter)} · {Count:N0}";
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>UI-02 Pustaka: cari nama/KUID, filter status, pilih aset → "Mau edit apa?" (AC-35).</summary>
public sealed class LibraryViewModel : ObservableObject, IPage
{
    public const int PageSize = 200;

    private readonly AppSession _session;
    private readonly Action<LibraryEntry> _open;
    private string _searchText = "";
    private AssetRow? _selected;
    private long _total;
    private long _matching;

    public LibraryViewModel(AppSession session, Action<LibraryEntry> open)
    {
        _session = session;
        _open = open;
        foreach (var f in Enum.GetValues<LibraryFilter>()) Filters.Add(new FilterChip(f) { IsSelected = f == LibraryFilter.All });
        SelectFilterCommand = new ParamCommand<FilterChip>(chip => { ActiveFilter = chip.Filter; Refresh(); });
        OpenCommand = new RelayCommand(() => { if (Selected is not null) _open(Selected.Entry); }, () => Selected is not null);
        Footer = new FooterAction("Mau ubah apa?", OpenCommand, () => Selected is null ? "Pilih aset dari daftar." : $"Aset terpilih: {Selected.Name}");
        Refresh();
    }

    public string Title => "Pustaka";
    public FooterAction Footer { get; }
    public string? BackLabel => null;
    public void Back() { }

    public ObservableCollection<FilterChip> Filters { get; } = new();
    public ObservableCollection<AssetRow> Results { get; } = new();
    public LibraryFilter ActiveFilter { get; private set; } = LibraryFilter.All;

    public string SearchText
    {
        get => _searchText;
        set { if (Set(ref _searchText, value ?? "")) Refresh(); }
    }

    public AssetRow? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OpenCommand.Refresh();
            Footer.Refresh();
        }
    }

    public ParamCommand<FilterChip> SelectFilterCommand { get; }
    public RelayCommand OpenCommand { get; }

    public string Summary => _total == 0 ? "Indeks kosong"
        : _matching > Results.Count ? $"{_total:N0} aset · menampilkan {Results.Count:N0} dari {_matching:N0} hasil"
        : $"{_total:N0} aset · {_matching:N0} hasil";

    /// <summary>Keadaan kosong (PRD 6.3); null bila ada hasil.</summary>
    public string? EmptyTitle => Results.Count > 0 ? null
        : _total == 0 ? "Belum ada data pustaka untuk profil ini."
        : "Tidak ada aset yang cocok.";

    public string? EmptyHint => Results.Count > 0 ? null
        : _total == 0 ? "Pengindeksan dari aplikasi belum tersedia. Sementara itu jalankan perintah 'railnexus index' di CLI, lalu buka Pustaka lagi."
        : Kuid.TryParse(SearchText, out _) ? "KUID ini tidak ada di indeks profil ini. Indeks mungkin belum diperbarui."
        : "Periksa ejaan, hapus filter status, atau tempel KUID lengkap seperti <kuid:123:456>.";

    public void Refresh()
    {
        var query = new LibraryQuery(SearchText, ActiveFilter, Limit: PageSize);
        _total = _session.Library.Count();
        _matching = _session.Library.Count(query);
        foreach (var chip in Filters)
        {
            chip.IsSelected = chip.Filter == ActiveFilter;
            chip.Count = _session.Library.Count(query with { Filter = chip.Filter });
        }

        var keep = Selected?.Entry.Kuid;
        Results.Clear();
        foreach (var e in _session.Library.Search(query)) Results.Add(new AssetRow(e));
        Selected = Results.FirstOrDefault(r => r.Entry.Kuid == keep);
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(ActiveFilter));
    }
}
