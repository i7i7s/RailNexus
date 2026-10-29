using System.Collections.ObjectModel;
using RailNexus.Core;
using RailNexus.Core.Texture;

namespace RailNexus.App.Core.ViewModels;

public sealed record Option<T>(T Value, string Label);

/// <summary>Satu gambar sumber di antrean Workshop Tekstur.</summary>
public sealed class TextureSourceItem : ObservableObject
{
    private TextureRole _role;
    private AlphaChoice _alpha = AlphaChoice.Auto;
    private string _outputName;
    private CollisionChoice _collision = CollisionChoice.Unresolved;
    private IReadOnlyList<NameCollision> _collisions = [];
    private TextureItemResult? _result;

    public TextureSourceItem(string path, Action changed)
    {
        SourcePath = path;
        _outputName = System.IO.Path.GetFileNameWithoutExtension(path);
        _role = GuessRole(_outputName);
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Role) or nameof(Alpha) or nameof(OutputName) or nameof(Collision)) changed();
        };
        try
        {
            var bytes = File.ReadAllBytes(path);
            var d = ImageCodec.Decode(bytes);
            Format = d.Ok ? d.Description : d.Format == ImageFormat.Unknown ? "Tidak dikenali" : d.Format.ToString().ToUpperInvariant();
            DecodeError = d.Ok ? null : d.Error;
            Preview = d.Image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Format = "Tidak dapat dibaca";
            DecodeError = ex.Message;
        }
    }

    public static IReadOnlyList<Option<TextureRole>> RoleOptions { get; } =
        Enum.GetValues<TextureRole>().Select(r => new Option<TextureRole>(r, TextureWorkshop.RoleLabel(r))).ToList();

    public static IReadOnlyList<Option<AlphaChoice>> AlphaOptions { get; } =
    [
        new(AlphaChoice.Auto, "Otomatis sesuai peran"),
        new(AlphaChoice.Keep, "Pertahankan alpha"),
        new(AlphaChoice.Discard, "Buang alpha (sengaja)"),
    ];

    public static IReadOnlyList<Option<CollisionChoice>> CollisionOptions { get; } =
    [
        new(CollisionChoice.Unresolved, "Belum dipilih"),
        new(CollisionChoice.Skip, "Lewati gambar ini"),
        new(CollisionChoice.Replace, "Ganti berkas lama (dengan backup)"),
    ];

    public string SourcePath { get; }
    public string FileName => System.IO.Path.GetFileName(SourcePath);
    public string Format { get; } = "";
    public string? DecodeError { get; }
    /// <summary>Gambar hasil dekode untuk preview di atas papan catur; null bila tidak terbaca.</summary>
    public RasterImage? Preview { get; }
    public string Size => Preview is null ? "" : $"{Preview.Width} × {Preview.Height}";
    public StatusLabel AlphaLabel => Preview is null ? new("Alpha belum diketahui", Tone.Unknown)
        : !Preview.HasAlpha ? new("Tanpa alpha", Tone.Mute)
        : Preview.AlphaIsOpaque() ? new("Alpha penuh (255)", Tone.Mute)
        : new("Memiliki alpha", Tone.Info);

    public TextureRole Role { get => _role; set => Set(ref _role, value); }
    public AlphaChoice Alpha { get => _alpha; set => Set(ref _alpha, value); }
    public string OutputName { get => _outputName; set => Set(ref _outputName, value ?? ""); }
    public CollisionChoice Collision { get => _collision; set => Set(ref _collision, value); }

    public IReadOnlyList<NameCollision> Collisions
    {
        get => _collisions;
        internal set { Set(ref _collisions, value); OnPropertyChanged(nameof(CollisionText)); }
    }

    public string? CollisionText => Collisions.Count == 0 ? null : string.Join(" ", Collisions.Select(c => c.Message));

    public TextureItemResult? Result
    {
        get => _result;
        internal set { Set(ref _result, value); OnPropertyChanged(nameof(ResultStatus)); OnPropertyChanged(nameof(ResultText)); }
    }

    public StatusLabel? ResultStatus => Result?.Status switch
    {
        TextureItemStatus.Ready => new($"Siap · {Result.OutputBits}-bit, dibaca ulang cocok", Tone.Ok),
        TextureItemStatus.Skipped => new("Dilewati", Tone.Mute),
        TextureItemStatus.Failed => new("Gagal", Tone.Err),
        _ => null,
    };

    public string? ResultText => Result is null ? null
        : string.Join(" ", new[] { Result.Error }.Concat(Result.Notes).Where(s => s.Length > 0));

    public TextureSource ToSource() => new(SourcePath, Role, Alpha, OutputName, Collision);

    /// <summary>Tebakan peran dari nama hanya saran awal (PRD 13.2 langkah 3); pengguna dapat mengubahnya.</summary>
    private static TextureRole GuessRole(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.EndsWith("_normal") || n.EndsWith("_n") || n.EndsWith("_nrm")) return TextureRole.Normal;
        if (n.EndsWith("_param") || n.EndsWith("_parameter") || n.EndsWith("_p")) return TextureRole.Parameter;
        return TextureRole.Color;
    }
}

public sealed class InventoryItem(InventoryFinding finding) : ObservableObject
{
    private bool _selected = finding.Kind == InventoryKind.ImageWithoutMetadata;
    public InventoryFinding Finding => finding;
    public string Path => finding.Path;
    public string Message => finding.Message;
    public bool CanCreateMetadata => finding.Kind == InventoryKind.ImageWithoutMetadata;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value && CanCreateMetadata); }
    public StatusLabel Kind => finding.Kind switch
    {
        InventoryKind.ImageWithoutMetadata => new("Tanpa metadata", Tone.Warn),
        InventoryKind.MissingTarget => new("Menunjuk berkas hilang", Tone.Err),
        _ => new("Metadata tidak terbaca", Tone.Err),
    };
}

/// <summary>
/// UI-14/15 Workshop Tekstur. Mode aset: hasil masuk draft bersama dan diterapkan lewat transaksi.
/// Mode folder: tanpa Trainz, hasil disimpan ke folder dan dilaporkan belum terpasang (PRD 22, AC-25).
/// </summary>
public sealed class TextureViewModel : ObservableObject, IPage
{
    private readonly AssetWorkspace? _workspace;
    private readonly TextureWorkshop _engine;
    private readonly Action? _back;
    private string _targetDirectory = "";
    private string? _exportFolder;
    private string? _message;

    public TextureViewModel(AssetWorkspace? workspace, string stagingRoot, Action? back = null, Action? openPbr = null)
    {
        OpenPbrCommand = new RelayCommand(() => openPbr?.Invoke(), () => openPbr is not null);
        _workspace = workspace;
        _engine = new TextureWorkshop(stagingRoot);
        _back = back;
        PrimaryCommand = new RelayCommand(Primary, () => PrimaryDisabledReason is null);
        MetadataCommand = new RelayCommand(CreateMetadata, () => _workspace?.Snapshot is not null && Inventory.Any(i => i.IsSelected));
        Footer = new FooterAction(IsAssetMode ? "Konversi ke draft" : "Simpan ke folder", PrimaryCommand,
            () => PrimaryDisabledReason ?? $"{Items.Count} gambar siap diproses");
        RefreshInventory();
    }

    public bool IsAssetMode => _workspace is not null;
    public string Title => "Workshop Tekstur";
    public FooterAction Footer { get; }
    public string? BackLabel => _back is null ? null : IsAssetMode ? "Kembali ke Mau edit apa?" : "Kembali ke Workshop";
    public void Back() => _back?.Invoke();

    public string ModeText => IsAssetMode
        ? "Hasil masuk draft aset aktif dan baru ditulis saat Terapkan & Selesai."
        : "Mode folder: hasil disimpan ke folder pilihan Anda. Folder itu belum terpasang di Trainz.";

    public ObservableCollection<TextureSourceItem> Items { get; } = new();
    public ObservableCollection<InventoryItem> Inventory { get; } = new();
    public RelayCommand PrimaryCommand { get; }
    public RelayCommand MetadataCommand { get; }
    public RelayCommand OpenPbrCommand { get; }
    public string? Message { get => _message; private set => Set(ref _message, value); }

    /// <summary>Subfolder tujuan di dalam aset/folder, mis. "textures". Kosong = akar.</summary>
    public string TargetDirectory
    {
        get => _targetDirectory;
        set { if (Set(ref _targetDirectory, value ?? "")) Revalidate(); }
    }

    public string? ExportFolder
    {
        get => _exportFolder;
        set { if (Set(ref _exportFolder, value)) Revalidate(); }
    }

    private IAssetFileView Existing => _workspace?.Snapshot is { } s ? s
        : _exportFolder is { Length: > 0 } f ? new FolderFileView(f)
        : DictionaryFileView.Empty;

    public string? PrimaryDisabledReason =>
        Items.Count == 0 ? "Tambahkan gambar terlebih dahulu."
        : IsAssetMode && _workspace!.Snapshot is null ? "Isi aset belum terbaca; buka ulang aset."
        : !IsAssetMode && string.IsNullOrEmpty(_exportFolder) ? "Pilih folder tujuan."
        : Items.Any(i => i.Collision != CollisionChoice.Skip && i.Preview is null) ? "Ada gambar yang tidak dapat dibaca; hapus atau lewati dulu."
        : Items.Any(i => i.Collisions.Count > 0 && Unresolved(i)) ? "Selesaikan benturan nama sebelum melanjutkan."
        : null;

    private static bool Unresolved(TextureSourceItem i) =>
        i.Collision != CollisionChoice.Skip && i.Collisions.Any(c => c.Kind != CollisionKind.ExistingFile || i.Collision != CollisionChoice.Replace);

    public void AddFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Items.Any(i => string.Equals(i.SourcePath, p, StringComparison.OrdinalIgnoreCase))) continue;
            var item = new TextureSourceItem(p, Revalidate);
            Items.Add(item);
        }
        Revalidate();
    }

    public void Remove(TextureSourceItem item)
    {
        Items.Remove(item);
        Revalidate();
    }

    /// <summary>Benturan dicek ulang setiap kali nama, pilihan, atau tujuan berubah (AC-18).</summary>
    private void Revalidate()
    {
        IReadOnlyList<TextureTarget> plan;
        try { plan = TextureWorkshop.PlanNames(Items.Select(i => i.ToSource()).ToList(), Existing, TargetDirectory); }
        catch (ArgumentException)
        {
            Message = "Folder tujuan harus berada di dalam aset, mis. 'textures'.";
            plan = [];
        }
        for (var i = 0; i < Items.Count; i++) Items[i].Collisions = i < plan.Count ? plan[i].Collisions : [];
        OnPropertyChanged(nameof(PrimaryDisabledReason));
        PrimaryCommand.Refresh();
        Footer.Refresh();
    }

    private void Primary()
    {
        var result = _engine.Prepare(Items.Select(i => i.ToSource()).ToList(), Existing, TargetDirectory);
        for (var i = 0; i < Items.Count && i < result.Items.Count; i++) Items[i].Result = result.Items[i];
        if (!result.Ok) { Message = result.Message; return; }
        if (result.Files.Count == 0) { Message = "Tidak ada hasil yang dapat dipakai. " + result.Message; return; }

        if (IsAssetMode)
        {
            foreach (var d in result.ToDraftItems()) _workspace!.Draft.Put(d);
            _workspace!.NotifyDraftChanged();
            Message = $"{result.Message} {result.Files.Count} berkas masuk draft.";
        }
        else
        {
            var (ok, msg, conflicts) = TextureWorkshop.ExportToFolder(result, _exportFolder!);
            Message = ok ? msg : $"{msg} {string.Join(", ", conflicts)}";
        }
        Revalidate();
    }

    private void RefreshInventory()
    {
        Inventory.Clear();
        if (_workspace?.Snapshot is not { } snap) return;
        foreach (var f in TextureInventory.Scan(snap, _workspace.Document))
        {
            var item = new InventoryItem(f);
            item.PropertyChanged += (_, _) => MetadataCommand.Refresh();
            Inventory.Add(item);
        }
        MetadataCommand.Refresh();
    }

    /// <summary>Alur B: metadata untuk gambar yang sudah ada di aset, tanpa konversi.</summary>
    private void CreateMetadata()
    {
        if (_workspace?.Snapshot is not { } snap) return;
        var selected = Inventory.Where(i => i.IsSelected).Select(i => i.Path).ToList();
        var result = _engine.PrepareMetadata(selected, snap);
        foreach (var d in result.ToDraftItems()) _workspace.Draft.Put(d);
        _workspace.NotifyDraftChanged();
        var failed = result.Items.Where(i => i.Status == TextureItemStatus.Failed).Select(i => $"{i.Target.ImagePath}: {i.Error}");
        Message = string.Join(" ", new[] { result.Message }.Concat(failed));
        foreach (var i in Inventory.Where(i => i.IsSelected)) i.IsSelected = false;
    }
}
