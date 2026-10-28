using System.Collections.ObjectModel;
using RailNexus.Core;
using RailNexus.Core.Texture;

namespace RailNexus.App.Core.ViewModels;

/// <summary>Satu kanal output: dari gambar (kanal tertentu) atau nilai konstan.</summary>
public sealed class ChannelSlot(string name, Action changed) : ObservableObject
{
    private string? _imagePath;
    private SourceChannel _channel = SourceChannel.R;
    private bool _useConstant = true;
    private int _constant;
    private string _meaning = "";

    public string Name => name;

    public static IReadOnlyList<Option<SourceChannel>> ChannelOptions { get; } =
    [
        new(SourceChannel.R, "Merah (R)"), new(SourceChannel.G, "Hijau (G)"), new(SourceChannel.B, "Biru (B)"),
        new(SourceChannel.A, "Alpha (A)"), new(SourceChannel.Luminance, "Kecerahan (dihitung)"),
    ];

    public string? ImagePath { get => _imagePath; set { if (Set(ref _imagePath, value)) { if (value is not null) UseConstant = false; changed(); } } }
    public SourceChannel Channel { get => _channel; set { if (Set(ref _channel, value)) changed(); } }
    public bool UseConstant { get => _useConstant; set { if (Set(ref _useConstant, value)) changed(); } }
    public int Constant { get => _constant; set { if (Set(ref _constant, Math.Clamp(value, 0, 255))) changed(); } }
    /// <summary>Arti kanal menurut pengguna, mis. "metallic"; hanya catatan, tidak memengaruhi data.</summary>
    public string Meaning { get => _meaning; set => Set(ref _meaning, value ?? ""); }

    public ChannelInput ToInput() => UseConstant || ImagePath is null ? ChannelInput.Const((byte)Constant) : new ChannelInput(ImagePath, Channel);
}

/// <summary>
/// UI-16 Perakit tekstur PBR (P1, RN-014): tetapkan sumber setiap kanal, rakit, baca ulang setiap kanal,
/// lalu masukkan ke draft atau simpan ke folder. Tidak mengubah material mesh legacy menjadi PBR.
/// </summary>
public sealed class PbrViewModel : ObservableObject, IPage
{
    private readonly AssetWorkspace? _workspace;
    private readonly TextureWorkshop _engine;
    private readonly Action? _back;
    private string _outputName = "parameter";
    private string _targetDirectory = "";
    private string? _exportFolder;
    private CollisionChoice _collision = CollisionChoice.Unresolved;
    private PackResult? _pack;
    private string? _message;

    public PbrViewModel(AssetWorkspace? workspace, string stagingRoot, Action? back = null)
    {
        _workspace = workspace;
        _engine = new TextureWorkshop(stagingRoot);
        _back = back;
        foreach (var n in new[] { "R", "G", "B", "A" }) Slots.Add(new ChannelSlot(n, Invalidate));
        PackCommand = new RelayCommand(Pack);
        PrimaryCommand = new RelayCommand(Primary, () => PrimaryDisabledReason is null);
        Footer = new FooterAction(IsAssetMode ? "Rakit ke draft" : "Simpan ke folder", PrimaryCommand, () => PrimaryDisabledReason ?? "Hasil rakitan sudah diperiksa per kanal.");
    }

    public bool IsAssetMode => _workspace is not null;
    public string Title => "Perakit tekstur PBR";
    public FooterAction Footer { get; }
    public string? BackLabel => _back is null ? null : "Kembali";
    public void Back() => _back?.Invoke();

    public string Hint =>
        "Tetapkan sumber setiap kanal secara eksplisit. Dokumentasi m.pbrmetal menyebut map parameter RGBA berisi emissive, roughness, " +
        "ambient occlusion, dan metallic; periksa urutan kanalnya pada dokumentasi build Anda, RailNexus tidak menebaknya. " +
        "Merakit map tidak mengubah material mesh legacy menjadi PBR.";

    public ObservableCollection<ChannelSlot> Slots { get; } = new();
    public RelayCommand PackCommand { get; }
    public RelayCommand PrimaryCommand { get; }
    public RasterImage? Preview => _pack?.Image;
    public IReadOnlyList<string> Report => _pack?.ChannelReport ?? [];
    public IReadOnlyList<string> Notes => _pack?.Notes ?? [];
    public string? Message { get => _message; private set => Set(ref _message, value); }

    public string OutputName { get => _outputName; set { if (Set(ref _outputName, value ?? "")) Invalidate(); } }
    public string TargetDirectory { get => _targetDirectory; set { if (Set(ref _targetDirectory, value ?? "")) Invalidate(); } }
    public string? ExportFolder { get => _exportFolder; set { if (Set(ref _exportFolder, value)) Invalidate(); } }
    public CollisionChoice Collision { get => _collision; set { if (Set(ref _collision, value)) Invalidate(); } }

    private IAssetFileView Existing => _workspace?.Snapshot is { } s ? s
        : _exportFolder is { Length: > 0 } f ? new FolderFileView(f) : DictionaryFileView.Empty;

    public string? CollisionText
    {
        get
        {
            try
            {
                var t = TextureWorkshop.PlanNames([new TextureSource(OutputName + ".rakitan", TextureRole.Parameter, AlphaChoice.Keep, OutputName, Collision)], Existing, TargetDirectory)[0];
                return t.Unresolved.Any() ? string.Join(" ", t.Unresolved.Select(c => c.Message)) : null;
            }
            catch (ArgumentException) { return "Folder tujuan harus berada di dalam aset."; }
        }
    }

    public string? PrimaryDisabledReason =>
        IsAssetMode && _workspace!.Snapshot is null ? "Isi aset belum terbaca; buka ulang aset."
        : !IsAssetMode && string.IsNullOrEmpty(_exportFolder) ? "Pilih folder tujuan."
        : _pack is not { Ok: true } ? "Rakit dan periksa hasilnya dulu."
        : CollisionText;

    private void Invalidate()
    {
        _pack = null;
        foreach (var n in new[] { nameof(Preview), nameof(Report), nameof(Notes), nameof(CollisionText), nameof(PrimaryDisabledReason) }) OnPropertyChanged(n);
        PrimaryCommand?.Refresh();
        Footer?.Refresh();
    }

    private void Pack()
    {
        _pack = ChannelPacker.Pack(Slots[0].ToInput(), Slots[1].ToInput(), Slots[2].ToInput(), Slots[3].ToInput());
        Message = _pack.Ok ? $"Rakitan {_pack.Image!.Width} × {_pack.Image.Height} siap diperiksa." : _pack.Error;
        foreach (var n in new[] { nameof(Preview), nameof(Report), nameof(Notes), nameof(PrimaryDisabledReason) }) OnPropertyChanged(n);
        PrimaryCommand.Refresh();
        Footer.Refresh();
    }

    private void Primary()
    {
        if (_pack is not { Ok: true, Image: { } image }) return;
        var meaning = string.Join(", ", Slots.Where(s => s.Meaning.Length > 0).Select(s => $"{s.Name}={s.Meaning}"));
        var result = _engine.PrepareImage(image, OutputName, meaning.Length > 0 ? $"Rakitan ({meaning})" : "Rakitan kanal", Existing, TargetDirectory, Collision);
        if (!result.Ok || result.Files.Count == 0) { Message = result.Items.FirstOrDefault()?.Error is { Length: > 0 } e ? e : result.Message; return; }
        if (IsAssetMode)
        {
            foreach (var d in result.ToDraftItems("Perakit PBR")) _workspace!.Draft.Put(d);
            _workspace!.NotifyDraftChanged();
            Message = $"{result.Message} {result.Files.Count} berkas masuk draft.";
        }
        else
        {
            var (ok, msg, conflicts) = TextureWorkshop.ExportToFolder(result, _exportFolder!);
            Message = ok ? msg : $"{msg} {string.Join(", ", conflicts)}";
        }
        OnPropertyChanged(nameof(CollisionText));
        OnPropertyChanged(nameof(PrimaryDisabledReason));
        PrimaryCommand.Refresh();
        Footer.Refresh();
    }
}
